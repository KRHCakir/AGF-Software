using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.DTOs;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class InvoiceController : ControllerBase
    {
        private readonly AppDbContext _context;
        public InvoiceController(AppDbContext context)
        {
            _context = context;
        }

        // KEEPING OLD ENDPOINT FOR NOW TO AVOID BREAKING EXISTING CODE
        // Ideally should be deprecated or updated to use nullable OrderId
        [HttpPost]
        public async Task<IActionResult> CreateInvoice(CreateInvoiceDto dto)
        {
            var order = await _context.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == dto.OrderId);
            if (order == null)
                return BadRequest("Order not found");

            var invoice = new Invoice
            {
                OrderId = dto.OrderId,
                CustomerId = order.CustomerId, // Backfill CustomerId
                InvoiceNumber = dto.InvoiceNumber,
                InvoiceDate = dto.InvoiceDate,
                TotalAmount = dto.TotalAmount,
                DueDate = dto.DueDate,
                Status = "Draft"
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            return Ok(invoice);
        }

        [HttpPost("manual")]
        public async Task<ActionResult<Invoice>> CreateManualInvoice([FromBody] CreateManualInvoiceDto dto)
        {
            var customer = await _context.Customers.FindAsync(dto.CustomerId);
            if (customer == null) return BadRequest("Customer not found");

            var invoice = new Invoice
            {
                CustomerId = dto.CustomerId,
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(100, 999)}", // Simple Auto-gen
                InvoiceDate = dto.InvoiceDate,
                DueDate = dto.DueDate ?? dto.InvoiceDate.AddDays(customer.PaymentTermsDays),
                Status = "Sent",
                CreatedAt = DateTime.UtcNow
            };

            decimal totalAmount = 0;
            decimal totalVat = 0;

            invoice.InvoiceLines = new List<InvoiceLine>();

            foreach (var lineDto in dto.Lines)
            {
                var line = new InvoiceLine
                {
                    ProductVariantId = lineDto.ProductVariantId,
                    Description = lineDto.Description,
                    Quantity = lineDto.Quantity,
                    UnitPrice = lineDto.UnitPrice,
                    VATPercentage = lineDto.VATPercentage
                    // LineTotal and Total are calculated properties
                };

                // Create "Shadow" Order for tracking history?
                // For now, let's JUST handle Inventory deduction
                if (lineDto.ProductVariantId.HasValue)
                {
                    var inventory = await _context.Inventory
                        .FirstOrDefaultAsync(i => i.ProductVariantId == lineDto.ProductVariantId.Value); // FIFO logic omitted for brevity

                    if (inventory != null)
                    {
                        inventory.Quantity -= lineDto.Quantity;
                        _context.Inventory.Update(inventory);
                        
                        // Check if stock goes negative for warning?
                    }
                     // Else: create negative inventory record?
                }
                
                // Calculate Totals manually for Invoice Header since EF mostly handles saving
                decimal lineTotal = line.Quantity * line.UnitPrice;
                decimal vatAmount = lineTotal * (line.VATPercentage / 100);
                
                totalAmount += lineTotal + vatAmount;
                totalVat += vatAmount;

                invoice.InvoiceLines.Add(line);
            }

            invoice.SubTotal = totalAmount - totalVat; // Back calculation or store explicitly
            invoice.VATAmount = totalVat;
            invoice.TotalAmount = totalAmount;

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetInvoices", new { id = invoice.Id }, invoice);
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoices()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Customer) // Load Customer directly
                .Include(i => i.Order)    // Optional: Load Linked Order if exists
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();
            
            // Map to DTO or return directly (serializing cycles handled in Program.cs)
            return Ok(invoices);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateInvoice(int id, [FromBody] UpdateInvoiceDto dto)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null) return NotFound();

            invoice.TotalAmount = dto.TotalAmount;
            invoice.Status = dto.Status;
            invoice.DueDate = dto.DueDate;

            await _context.SaveChangesAsync();
            return Ok(invoice);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteInvoice(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                return NotFound();

            _context.Invoices.Remove(invoice);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GetInvoicePdf(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.InvoiceLines)
                .ThenInclude(l => l.ProductVariant)
                .ThenInclude(pv => pv.Product)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null)
                return NotFound();

            var bytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Header()
                        .Text($"Invoice {invoice.InvoiceNumber ?? invoice.Id.ToString()}")
                        .FontSize(20)
                        .SemiBold();

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text($"Customer: {invoice.Customer.Name}");
                        col.Item().Text($"Date: {invoice.InvoiceDate:yyyy-MM-dd}");
                        
                        // Line Items Table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Description
                                columns.RelativeColumn(1); // Qty
                                columns.RelativeColumn(1); // Price
                                columns.RelativeColumn(1); // Total
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Description").SemiBold();
                                header.Cell().Text("Qty").SemiBold();
                                header.Cell().Text("Price").SemiBold();
                                header.Cell().Text("Total").SemiBold();
                            });

                            foreach (var line in invoice.InvoiceLines)
                            {
                                string desc = line.Description;
                                if (line.ProductVariant != null)
                                    desc = $"{line.ProductVariant.Product.Name} - {line.ProductVariant.Name}";

                                table.Cell().Text(desc);
                                table.Cell().Text(line.Quantity.ToString());
                                table.Cell().Text($"€ {line.UnitPrice:0.00}");
                                table.Cell().Text($"€ {(line.Quantity * line.UnitPrice):0.00}");
                            }
                        });


                        col.Item().Text($"Total Amount: € {invoice.TotalAmount:0.00}").FontSize(14).SemiBold().AlignRight();
                    });

                    page.Footer().AlignCenter().Text($"Generated {System.DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                });
            }).GeneratePdf();

            var fileName = $"Invoice-{(invoice.InvoiceNumber ?? invoice.Id.ToString())}.pdf";
            return File(bytes, "application/pdf", fileName);
        }
    }
}
