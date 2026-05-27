using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IncomingInvoicesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailIngestionService _emailService;

        public IncomingInvoicesController(AppDbContext context, IEmailIngestionService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // GET: api/IncomingInvoices
        [HttpGet]
        public async Task<ActionResult<IEnumerable<IncomingInvoice>>> GetIncomingInvoices()
        {
            return await _context.IncomingInvoices
                .Include(i => i.Vendor)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        // GET: api/IncomingInvoices/vendors
        [HttpGet("vendors")]
        public async Task<ActionResult<IEnumerable<Vendor>>> GetVendors()
        {
            return await _context.Vendors.ToListAsync();
        }

        // GET: api/IncomingInvoices/5
        [HttpGet("{id}")]
        public async Task<ActionResult<IncomingInvoice>> GetIncomingInvoice(int id)
        {
            var invoice = await _context.IncomingInvoices
                .Include(i => i.Vendor)
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null)
            {
                return NotFound();
            }

            return invoice;
        }

        // POST: api/IncomingInvoices/upload
        [HttpPost("upload")]
        public async Task<ActionResult<IncomingInvoice>> UploadInvoice(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Simulation: Save file to temp path
            var tempPath = Path.GetTempFileName() + Path.GetExtension(file.FileName);
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Process
            var invoice = await _emailService.ProcessEmailAttachmentAsync(tempPath, "manual-upload@user.com");
            
            return CreatedAtAction("GetIncomingInvoice", new { id = invoice.Id }, invoice);
        }

        // POST: api/IncomingInvoices/manual
        [HttpPost("manual")]
        public async Task<ActionResult<IncomingInvoice>> CreateManualInvoice([FromBody] AgfAdmin.DTOs.CreateIncomingInvoiceDto dto)
        {
            // 1. Create Invoice
            var invoice = new IncomingInvoice
            {
                VendorId = dto.VendorId,
                InvoiceNumber = dto.InvoiceNumber,
                InvoiceDate = dto.InvoiceDate,
                DueDate = dto.DueDate ?? dto.InvoiceDate.AddDays(30),
                Status = "Approved", // Manual entry usually implies it's verified
                ReviewConfidence = 1.0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            decimal totalAmount = 0;
            decimal totalVat = 0;

            // 2. Add Lines
            invoice.InvoiceLines = new List<IncomingInvoiceLine>();
            foreach (var lineDto in dto.Lines)
            {
                var line = new IncomingInvoiceLine
                {
                    Description = lineDto.Description,
                    Quantity = lineDto.Quantity,
                    UnitPrice = lineDto.UnitPrice,
                    VATPercentage = lineDto.VATPercentage,
                    LineTotal = lineDto.Quantity * lineDto.UnitPrice
                };
                
                // Calculate VAT
                line.VATAmount = line.LineTotal * (line.VATPercentage / 100);
                
                totalAmount += line.LineTotal + line.VATAmount;
                totalVat += line.VATAmount;

                invoice.InvoiceLines.Add(line);

                // 3. Update Inventory if ProductVariantId is present (Optional: Incoming = Stock Increase)
                if (lineDto.ProductVariantId.HasValue)
                {
                    var inventory = await _context.Inventory
                        .FirstOrDefaultAsync(i => i.ProductVariantId == lineDto.ProductVariantId.Value);

                    if (inventory != null)
                    {
                        inventory.Quantity += lineDto.Quantity;
                        _context.Inventory.Update(inventory);
                    }
                    else
                    {
                         _context.Inventory.Add(new Inventory
                        {
                            ProductVariantId = lineDto.ProductVariantId.Value,
                            Location = "Warehouse A",
                            Quantity = lineDto.Quantity,
                            Stored = DateTime.UtcNow
                        });
                    }
                }
            }

            invoice.TotalAmount = totalAmount;
            invoice.VATAmount = totalVat;

            _context.IncomingInvoices.Add(invoice);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetIncomingInvoice", new { id = invoice.Id }, invoice);
        }

        // PUT: api/IncomingInvoices/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateIncomingInvoice(int id, IncomingInvoice invoice)
        {
            if (id != invoice.Id)
            {
                return BadRequest();
            }

            _context.Entry(invoice).State = EntityState.Modified;

            // Also update lines if sent? Complex, generally handle lines separately or use specific DTOs.
            // For prototype, basic update.

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!IncomingInvoiceExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
        
        // POST: api/IncomingInvoices/5/approve
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveInvoice(int id)
        {
            var invoice = await _context.IncomingInvoices.FindAsync(id);
            if (invoice == null) return NotFound();

            invoice.Status = "Approved";
            // Trigger payment workflow or creating internal record
            await _context.SaveChangesAsync();
            return Ok(new { status = "Approved" });
        }

        // POST: api/IncomingInvoices/5/reject
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectInvoice(int id)
        {
            var invoice = await _context.IncomingInvoices.FindAsync(id);
            if (invoice == null) return NotFound();

            invoice.Status = "Rejected";
            await _context.SaveChangesAsync();
            return Ok(new { status = "Rejected" });
        }

        private bool IncomingInvoiceExists(int id)
        {
            return _context.IncomingInvoices.Any(e => e.Id == id);
        }
    }
}
