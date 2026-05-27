using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Models;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public InventoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetInventory()
        {
            var inv = await _context.Inventory
                .Include(i => i.ProductVariant)
                    .ThenInclude(pv => pv.Product)
                .Include(i => i.Shipment)
                .Select(i => new {
                    i.Id,
                    i.ProductVariantId,
                    ProductVariantName = i.ProductVariant.Product.Name + " " + i.ProductVariant.Name,
                    i.Quantity,
                    i.Location,
                    i.Stored,
                    i.ShipmentId,
                    i.IsFrozen,
                    Shipment = i.Shipment != null ? new { i.Shipment.ContainerCode, i.Shipment.Status, i.Shipment.Origin } : null
                })
                .ToListAsync();

            return Ok(inv);
        }

        public class DumpWasteDto
        {
            public int QuantityDumped { get; set; }
            public decimal? WeightDumpedKg { get; set; }
            public string Reason { get; set; }
        }

        [HttpPost("{id}/waste")]
        public async Task<IActionResult> LogWaste(int id, [FromBody] DumpWasteDto dto)
        {
            if (dto.QuantityDumped <= 0) return BadRequest("Quantity dumped must be greater than 0");

            var inv = await _context.Inventory.FindAsync(id);
            if (inv == null) return NotFound($"Inventory batch {id} not found");

            if (inv.Quantity < dto.QuantityDumped) return BadRequest("Cannot dump more than current stock quantity");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try {
                // Deduct from physical stock
                inv.Quantity -= dto.QuantityDumped;
                // proportional weight deduction if strictly tracked? We'll leave that to the frontend to pass or we just leave the total weight alone. 
                // Wait, if TotalWeightKg is 950 and we dump some, TotalWeightKg should reduce. But we will let the user pass WeightDumpedKg.
                if (inv.TotalWeightKg.HasValue && dto.WeightDumpedKg.HasValue) 
                {
                    inv.TotalWeightKg -= dto.WeightDumpedKg.Value;
                }
                
                _context.Inventory.Update(inv);

                var wasteLog = new WasteLog
                {
                    InventoryId = inv.Id,
                    ProductVariantId = inv.ProductVariantId,
                    QuantityDumped = dto.QuantityDumped,
                    WeightDumpedKg = dto.WeightDumpedKg,
                    Reason = dto.Reason
                };

                _context.WasteLogs.Add(wasteLog);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { Message = "Waste securely logged and deducted." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Waste mapping failed: {ex.Message}");
            }
        }

        [HttpPost("{id}/freeze")]
        public async Task<IActionResult> FreezeBatch(int id)
        {
            var inv = await _context.Inventory.FindAsync(id);
            if (inv == null) return NotFound("Batch not found.");

            inv.IsFrozen = true;
            _context.Inventory.Update(inv);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Batch successfully frozen against automatic sales." });
        }

        [HttpPost("{id}/unfreeze")]
        public async Task<IActionResult> UnfreezeBatch(int id)
        {
            var inv = await _context.Inventory.FindAsync(id);
            if (inv == null) return NotFound("Batch not found.");

            inv.IsFrozen = false;
            _context.Inventory.Update(inv);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Batch restrictions lifted." });
        }
    }
}
