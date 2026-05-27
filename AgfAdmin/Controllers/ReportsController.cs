using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgfAdmin.Data;
using System.Linq;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("trace/shipment/{shipmentId}")]
        public async Task<IActionResult> TraceShipmentForward(int shipmentId)
        {
            var batches = await _context.Inventory
                .Where(i => i.ShipmentId == shipmentId)
                .Select(i => i.Id)
                .ToListAsync();

            var allocs = await _context.OrderLineAllocations
                .Include(a => a.OrderLine).ThenInclude(ol => ol.Order).ThenInclude(o => o.Customer)
                .Where(a => batches.Contains(a.InventoryId))
                .ToListAsync();

            var waste = await _context.WasteLogs
                .Where(w => batches.Contains(w.InventoryId))
                .ToListAsync();

            var orderIds = allocs.Where(a => a.OrderLine?.Order != null).Select(a => a.OrderLine.Order.Id).Distinct().ToList();
            var invoicesDict = await _context.Invoices.Where(i => i.OrderId.HasValue && orderIds.Contains(i.OrderId.Value)).ToDictionaryAsync(i => i.OrderId.Value, i => i.InvoiceNumber);

            var customersExposed = allocs
                .Where(a => a.OrderLine?.Order != null)
                .Select(a => new {
                    CustomerName = a.OrderLine.Order.Customer?.Name ?? a.OrderLine.Order.CustomCustomerName ?? "Unknown",
                    OrderId = a.OrderLine.Order.Id,
                    InvoiceNumber = invoicesDict.ContainsKey(a.OrderLine.Order.Id) ? invoicesDict[a.OrderLine.Order.Id] : "Pending",
                    QuantityAllocated = a.AllocatedQuantity,
                    DateSold = a.AllocatedAt
                })
                .OrderBy(x => x.DateSold)
                .ToList();

            return Ok(new {
                ShipmentId = shipmentId,
                TotalBatches = batches.Count,
                TotalWastedUnits = waste.Sum(w => w.QuantityDumped),
                WastedReasons = waste.GroupBy(w => w.Reason).Select(g => new { Reason = g.Key, Count = g.Sum(w => w.QuantityDumped) }),
                Customers = customersExposed
            });
        }

        [HttpGet("trace/invoice/{invoiceId}")]
        public async Task<IActionResult> TraceInvoiceBackward(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Order).ThenInclude(o => o.Lines).ThenInclude(l => l.Allocations).ThenInclude(a => a.Inventory).ThenInclude(inv => inv.Shipment)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice?.Order == null) return NotFound("Invoice or Order not found.");

            var sources = invoice.Order.Lines
                .SelectMany(l => l.Allocations)
                .Select(a => new {
                    ProductName = a.OrderLine.ProductVariantId.ToString(), // Or map name properly
                    BatchQuantity = a.AllocatedQuantity,
                    InventoryBatchId = a.InventoryId,
                    ShipmentId = a.Inventory?.ShipmentId,
                    ContainerCode = a.Inventory?.Shipment?.ContainerCode ?? "Unknown / Local Ghost",
                    Origin = a.Inventory?.Shipment?.Origin ?? "Unknown"
                })
                .ToList();

            return Ok(new {
                InvoiceId = invoiceId,
                InvoiceNumber = invoice.InvoiceNumber,
                Sources = sources
            });
        }
    }
}
