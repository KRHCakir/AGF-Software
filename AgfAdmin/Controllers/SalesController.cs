using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : ControllerBase
    {
        private readonly ISalesParserService _parserService;
        private readonly AppDbContext _context;

        public SalesController(ISalesParserService parserService, AppDbContext context)
        {
            _parserService = parserService;
            _context = context;
        }

        [HttpGet("customers")]
        public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
        {
            return await _context.Customers.ToListAsync();
        }

        [HttpPost("parse")]
        public async Task<ActionResult<ParseOrderResponse>> ParseOrder([FromBody] ParseOrderRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RawText))
            {
                return BadRequest("Text is required");
            }

            var result = await _parserService.ParseOrderTextAsync(request.RawText);
            return Ok(result);
        }

        [HttpPost("confirm")]
        public async Task<ActionResult<List<int>>> ConfirmOrder([FromBody] ConfirmOrderRequest request)
        {
            if (request.CustomerId == null && string.IsNullOrWhiteSpace(request.CustomCustomerName))
            {
                return BadRequest("Either a Customer ID or a Custom Customer Name is required.");
            }

            if (request.CustomerId != null)
            {
                var customer = await _context.Customers.FindAsync(request.CustomerId);
                if (customer == null) return BadRequest($"Customer {request.CustomerId} not found.");
            }

            var createdOrderIds = new List<int>();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Create the Master Order
                var order = new Order
                {
                    CustomerId = request.CustomerId,
                    CustomCustomerName = request.CustomCustomerName,
                    Status = "Confirmed",
                    CreatedAt = DateTime.UtcNow,
                    Lines = new List<OrderLine>()
                };

                _context.Orders.Add(order);

                foreach (var item in request.Items)
                {
                    // Fallback to variant default price if AgreedPrice is null
                    decimal unitPrice = item.AgreedPrice ?? 0m;
                    if (item.AgreedPrice == null)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.ProductVariantId);
                        if (variant != null) unitPrice = variant.DefaultPrice;
                    }

                    // 2. Add Line
                    var orderLine = new OrderLine
                    {
                        ProductVariantId = item.ProductVariantId,
                        Quantity = item.Quantity,
                        UnitPrice = unitPrice
                    };
                    order.Lines.Add(orderLine);

                    // 3. Update Inventory & Track Batch Allocations (FIFO Auto-Allocation)
                    var inventoryQuery = _context.Inventory
                        .Where(i => i.ProductVariantId == item.ProductVariantId && i.Quantity > 0);

                    // If manual override triggered, restrict deductive loop to the explicit batch
                    if (item.TargetShipmentId.HasValue)
                    {
                        inventoryQuery = inventoryQuery.Where(i => i.ShipmentId == item.TargetShipmentId.Value);
                    }
                    else
                    {
                        // Exclude frozen batches from automatic FIFO draining
                        inventoryQuery = inventoryQuery.Where(i => !i.IsFrozen);
                    }

                    var inventoryRecords = await inventoryQuery
                        .OrderBy(i => i.Stored) // FIFO Auto-Allocate (Oldest First)
                        .ToListAsync();

                    int remainingToDeduct = item.Quantity;
                    foreach (var inv in inventoryRecords)
                    {
                        if (remainingToDeduct <= 0) break;
                        
                        var deduct = Math.Min(inv.Quantity, remainingToDeduct);
                        inv.Quantity -= deduct;
                        remainingToDeduct -= deduct;
                        _context.Inventory.Update(inv);

                        // Relational tracking to the origin Shipment/Batch
                        var allocation = new OrderLineAllocation
                        {
                            OrderLine = orderLine,
                            Inventory = inv,
                            AllocatedQuantity = deduct,
                            AllocatedAt = DateTime.UtcNow
                        };
                        _context.OrderLineAllocations.Add(allocation);
                    }

                    // If still remaining (negative backorder conceptually)
                    if (remainingToDeduct > 0)
                    {
                        var newInv = new Inventory
                        {
                            ProductVariantId = item.ProductVariantId,
                            Location = "Backorder",
                            Quantity = -remainingToDeduct,
                            Stored = DateTime.UtcNow
                        };
                        _context.Inventory.Add(newInv);

                        var backorderAllocation = new OrderLineAllocation
                        {
                            OrderLine = orderLine,
                            Inventory = newInv,
                            AllocatedQuantity = remainingToDeduct,
                            AllocatedAt = DateTime.UtcNow
                        };
                        _context.OrderLineAllocations.Add(backorderAllocation);
                    }
                }

                await _context.SaveChangesAsync();
                createdOrderIds.Add(order.Id);
                await transaction.CommitAsync();

                return Ok(createdOrderIds);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Error creating order: {ex.Message}");
            }
        }
    }
}
