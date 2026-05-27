using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgfAdmin.Data;
using AgfAdmin.Models;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShipmentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ShipmentsController(AppDbContext context)
        {
            _context = context;
        }

        public class ReceiveShipmentDto
        {
            public int ProductVariantId { get; set; }
            public int QuantityReceived { get; set; }
            public decimal? TotalWeightKg { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetShipments()
        {
            var shipments = await _context.Shipments.OrderByDescending(s => s.CreatedAt).ToListAsync();
            return Ok(shipments);
        }

        [HttpPost("{id}/receive")]
        public async Task<IActionResult> ReceiveShipment(int id, [FromBody] ReceiveShipmentDto dto)
        {
            var shipment = await _context.Shipments.FindAsync(id);
            if (shipment == null) return NotFound($"Shipment {id} not found.");

            var variant = await _context.ProductVariants.FindAsync(dto.ProductVariantId);
            if (variant == null) return BadRequest($"Product Variant {dto.ProductVariantId} not found.");

            // 1. Scan for Ghost Backorders
            var ghostBackorders = await _context.Inventory
                .Where(i => i.ProductVariantId == dto.ProductVariantId && i.Quantity < 0 && i.Location == "Backorder")
                .OrderBy(i => i.Stored) // Oldest debt first
                .ToListAsync();

            int remainingIntake = dto.QuantityReceived;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var ghost in ghostBackorders)
                {
                    if (remainingIntake <= 0) break;

                    // Ghost quantity is negative (e.g., -20)
                    int debtToResolve = Math.Abs(ghost.Quantity);
                    int appliedIntake = Math.Min(debtToResolve, remainingIntake);

                    // Update Ghost
                    ghost.Quantity += appliedIntake;
                    remainingIntake -= appliedIntake;
                    _context.Inventory.Update(ghost);

                    // Re-wire the existing static Allocations to point to THIS legitimate new Shipment
                    var tiedAllocations = await _context.OrderLineAllocations
                        .Where(a => a.InventoryId == ghost.Id)
                        .ToListAsync();

                    foreach (var alloc in tiedAllocations)
                    {
                        // Create a parallel legitimate Inventory row bound strictly to this exact Shipment
                        // just for the portion of the allocation that was fixed, assuming the allocation is fully resolved by this batch
                        // Wait, a single Ghost can cover multiple allocations. The mathematically clean way:
                        // Convert the portion of the ghost that is resolved into a true Inventory row tied to the shipment.
                    }
                    
                    // Actually, the simplest architectural solution to avoid splitting OrderLineAllocations endlessly:
                    // Since a 'Ghost' represents a single monolithic negative pool created during an order,
                    // If we just attach the `ShipmentId` directly onto the Ghost row and rename its Location,
                    // we immediately validate all 'floating' Allocations pointing at it natively!
                    
                    if (ghost.Quantity == 0)
                    {
                        // Ghost fully resolved
                        ghost.Location = $"Resolved via Shipment {id}";
                        ghost.ShipmentId = id;
                        _context.Inventory.Update(ghost);
                    }
                    else
                    {
                        // Ghost partially resolved.
                        // We must split the ghost into two. The resolved piece becomes a valid shipment tracing pool.
                        var resolvedPool = new Inventory
                        {
                            ProductVariantId = ghost.ProductVariantId,
                            Location = $"Resolved via Shipment {id}",
                            Quantity = 0, // It's empty because the allocation immediately ate it
                            ShipmentId = id,
                            Stored = ghost.Stored
                        };
                        _context.Inventory.Add(resolvedPool);
                        await _context.SaveChangesAsync(); // Get ID

                        // Shift proportional Allocations pointing currently at `ghost.Id` -> `resolvedPool.Id`
                        var allocs = await _context.OrderLineAllocations.Where(a => a.InventoryId == ghost.Id).ToListAsync();
                        int shiftTarget = appliedIntake;

                        foreach (var alloc in allocs)
                        {
                            if (shiftTarget <= 0) break;
                            
                            if (alloc.AllocatedQuantity <= shiftTarget)
                            {
                                alloc.InventoryId = resolvedPool.Id;
                                shiftTarget -= alloc.AllocatedQuantity;
                                _context.OrderLineAllocations.Update(alloc);
                            }
                            else
                            {
                                // The allocation is bigger than the intake piece. Split the allocation.
                                var splitAlloc = new OrderLineAllocation
                                {
                                    OrderLineId = alloc.OrderLineId,
                                    InventoryId = resolvedPool.Id,
                                    AllocatedQuantity = shiftTarget,
                                    AllocatedAt = alloc.AllocatedAt
                                };
                                _context.OrderLineAllocations.Add(splitAlloc);
                                
                                alloc.AllocatedQuantity -= shiftTarget;
                                _context.OrderLineAllocations.Update(alloc);
                                shiftTarget = 0;
                            }
                        }
                    }
                }

                // 2. Add remaining clean stock as standard active Physical Inventory
                if (remainingIntake > 0)
                {
                    decimal? calculatedWeight = null;
                    if (dto.TotalWeightKg.HasValue)
                    {
                        // Deduct proportional weight for the ghosts? For simplicity, we assign remaining calculated weight relative to quantity
                        calculatedWeight = dto.TotalWeightKg.Value * ((decimal)remainingIntake / (decimal)dto.QuantityReceived);
                    }

                    var newStock = new Inventory
                    {
                        ProductVariantId = dto.ProductVariantId,
                        Quantity = remainingIntake,
                        Location = "Warehouse A", // Or parameterized
                        ShipmentId = id,
                        Stored = DateTime.UtcNow,
                        TotalWeightKg = calculatedWeight
                    };
                    _context.Inventory.Add(newStock);
                }

                shipment.Status = "Received";
                _context.Shipments.Update(shipment);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { Message = $"Shipment {id} mapped. {dto.QuantityReceived - remainingIntake} units swallowed by backorders. {remainingIntake} units added to live stock." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Ghost sweep failure: {ex.Message}");
            }
        }
    }
}
