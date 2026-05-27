using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.DTOs;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class OrderController : ControllerBase
    {
        private readonly AppDbContext _context;
        public OrderController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
        {
            int finalCustomerId;

            // 1. Heeft de frontend een bestaand ID gestuurd?
            if (dto.CustomerId.HasValue && dto.CustomerId.Value > 0)
            {
                var customer = await _context.Customers.FindAsync(dto.CustomerId.Value);
                if (customer == null) return BadRequest("Customer not found");
                finalCustomerId = customer.Id;
            }
            // 2. Geen ID? Dan is het een NIEUWE klant! Auto-aanmaken:
            else if (!string.IsNullOrWhiteSpace(dto.CustomerName))
            {
                var newCustomer = new Customer 
                { 
                    Name = dto.CustomerName.Trim(),
                    PaymentTermsDays = 14, // Standaard betaaltermijn
                    CreatedAt = DateTime.UtcNow
                };
                _context.Customers.Add(newCustomer);
                await _context.SaveChangesAsync(); // Sla direct op om een ID te genereren
                
                finalCustomerId = newCustomer.Id; // Pak het kersverse ID
            }
            else
            {
                return BadRequest("Geen klant opgegeven.");
            }

            // 3. Bouw de order verder met het finalCustomerId
            var order = new Order
            {
                CustomerId = finalCustomerId,
                ContainerCode = dto.ContainerCode,
                Status = "Pending",
                Lines = new List<OrderLine>()
            };

            foreach (var lineDto in dto.Lines)
            {
                var variant = await _context.ProductVariants.FindAsync(lineDto.ProductVariantId);
                if (variant == null) return BadRequest($"Product Variant {lineDto.ProductVariantId} not found");

                order.Lines.Add(new OrderLine
                {
                    ProductVariantId = lineDto.ProductVariantId,
                    Quantity = lineDto.Quantity,
                    UnitPrice = lineDto.UnitPrice ?? variant.DefaultPrice
                });
            }

            if (!order.Lines.Any()) return BadRequest("Order must have at least one line.");

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return Ok(new { Id = order.Id, Status = order.Status });
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            var orders = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.Inspection)
                .Include(o => o.Lines)
                    .ThenInclude(l => l.ProductVariant)
                        .ThenInclude(pv => pv.Product)
                .OrderByDescending(o => o.CreatedAt) // Sorteer standaard op nieuwste eerst in backend
                .Select(o => new OrderDto
                {
                    Id = o.Id,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomCustomerName ?? (o.Customer != null ? o.Customer.Name : "Walk-in"),
                    TotalAmount = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
                    ContainerCode = o.ContainerCode,
                    Status = o.Status,
                    CreatedAt = o.CreatedAt,
                    Inspection = o.Inspection != null ? new InspectionDto
                    {
                        Score = o.Inspection.Score,
                        Comment = o.Inspection.Comment,
                        Inspector = o.Inspection.Inspector
                    } : null,
                    Lines = o.Lines.Select(l => new OrderLineDto
                    {
                        Id = l.Id,
                        ProductVariantId = l.ProductVariantId,
                        ProductName = l.ProductVariant.Product.Name + " " + l.ProductVariant.Name,
                        Origin = l.ProductVariant.Origin,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        LineTotal = l.Quantity * l.UnitPrice
                    }).ToList()
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPost("{id}/generate-invoice")]
        public async Task<IActionResult> GenerateInvoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Lines)
                    .ThenInclude(l => l.ProductVariant)
                        .ThenInclude(pv => pv.Product)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound("Order not found");

            if (order.Status == "Invoiced")
                return BadRequest("Order has already been invoiced");

            // 1. Create the Invoice
            var invoice = new Invoice
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                CustomCustomerName = order.CustomCustomerName,
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{order.Id}",
                InvoiceDate = DateTime.UtcNow,
                Status = "Sent",
                DueDate = DateTime.UtcNow.AddDays(order.Customer?.PaymentTermsDays > 0 ? order.Customer.PaymentTermsDays : 14),
                InvoiceLines = new List<InvoiceLine>()
            };

            var vatPercentage = 0.09m; // Assume 9% VAT for food
            decimal subTotal = 0;

            foreach (var line in order.Lines)
            {
                var lineTotal = line.Quantity * line.UnitPrice;
                subTotal += lineTotal;

                invoice.InvoiceLines.Add(new InvoiceLine
                {
                    ProductVariantId = line.ProductVariantId,
                    Description = line.ProductVariant.Product.Name + " " + line.ProductVariant.Name,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VATPercentage = vatPercentage * 100
                });
            }

            var vatAmount = subTotal * vatPercentage;
            invoice.SubTotal = subTotal;
            invoice.VATAmount = vatAmount;
            invoice.TotalAmount = subTotal + vatAmount;

            _context.Invoices.Add(invoice);

            // 3. Update Order Status
            order.Status = "Invoiced";

            await _context.SaveChangesAsync();

            return Ok(new { InvoiceNumber = invoice.InvoiceNumber });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound("Order not found");

            if (order.Status == "Invoiced") 
                return BadRequest("Cannot delete an order that has already been Invoiced.");

            // Virtual vs Physical Stock Reversion Sequence
            foreach (var line in order.Lines)
            {
                var allocations = await _context.OrderLineAllocations
                    .Where(a => a.OrderLineId == line.Id)
                    .Include(a => a.Inventory)
                    .ToListAsync();

                foreach (var alloc in allocations)
                {
                    // Restore physical stock bound to this historical shipment batch
                    alloc.Inventory.Quantity += alloc.AllocatedQuantity;
                    _context.Inventory.Update(alloc.Inventory);
                }
                
                _context.OrderLineAllocations.RemoveRange(allocations);
            }

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            return Ok(new { Message = $"Order {id} and all tied stock allocations have been safely reverted." });
        }
    }
}