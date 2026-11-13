using AgfAdmin.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace AgfAdmin.Controllers
{
    [Authorize(Policy = Permissions.AdminWrites)]
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AgfAdmin.Services.PartijService _partijen;

        public SettingsController(AppDbContext context, AgfAdmin.Services.PartijService partijen)
        {
            _context = context;
            _partijen = partijen;
        }

        // Partijnummering: modus (Auto of Own), formaat en wanneer het volgnummer opnieuw telt. Lezen mag iedereen, wijzigen met Beheer.
        // AI-lezen (Beheer): aan/uit met de bevestiging dat de klant weet dat documenten naar een externe dienst gaan, en de maandlimiet.
        [HttpGet("ai-reading")]
        public async Task<IActionResult> GetAiReading([FromServices] Services.DocumentAi.AiInvoiceReadingService ai) =>
            Ok(await ai.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("ai-reading")]
        public async Task<IActionResult> UpdateAiReading([FromServices] Services.DocumentAi.AiInvoiceReadingService ai, [FromBody] AgfAdmin.DTOs.UpdateAiReadingSettingsDto request) =>
            this.ToActionResult(await ai.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));

        // Laadbon (documenten deel 1): taal van de kopteksten, klantnaam en leverancierskolom.
        [HttpGet("loading-slip")]
        public async Task<IActionResult> GetLoadingSlip([FromServices] Services.LoadingSlipService slips) => Ok(await slips.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("loading-slip")]
        public async Task<IActionResult> UpdateLoadingSlip([FromServices] Services.LoadingSlipService slips, [FromBody] AgfAdmin.DTOs.LoadingSlipSettingsDto request) =>
            this.ToActionResult(await slips.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));

        // Transportopdracht (documenten deel 2): taal van de kopteksten en de standaard soort pallet.
        [HttpGet("transport-order")]
        public async Task<IActionResult> GetTransportOrder([FromServices] Services.SalesOrderTransportService transports) => Ok(await transports.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("transport-order")]
        public async Task<IActionResult> UpdateTransportOrder([FromServices] Services.SalesOrderTransportService transports, [FromBody] AgfAdmin.DTOs.TransportOrderSettingsDto request) =>
            this.ToActionResult(await transports.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));

        // De e-mailadressen van de loods (documenten deel 3): de laadbon gaat daarheen.
        [HttpGet("warehouse-recipients")]
        public async Task<IActionResult> GetWarehouseRecipients([FromServices] Services.DocumentRecipientService recipients) => Ok(await recipients.GetWarehouseAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("warehouse-recipients")]
        public async Task<IActionResult> SaveWarehouseRecipients([FromServices] Services.DocumentRecipientService recipients, [FromBody] AgfAdmin.DTOs.SaveDocumentRecipientsDto request) =>
            this.ToActionResult(await recipients.SaveWarehouseAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpGet("partij-numbering")]
        public async Task<IActionResult> GetPartijNumbering() => Ok(await _partijen.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("partij-numbering")]
        public async Task<IActionResult> UpdatePartijNumbering([FromBody] AgfAdmin.DTOs.PartijSettingsDto request) =>
            this.ToActionResult(await _partijen.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var settings = await _context.CompanySettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // Als het bedrijf nog nooit instellingen heeft opgeslagen, maak er dan nu eentje aan!
                settings = new CompanySettings();
                _context.CompanySettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return Ok(settings);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromForm] CompanySettingsDto dto)
        {
            var settings = await _context.CompanySettings.FirstOrDefaultAsync();
            if (settings == null) return NotFound();

            // Update alle tekstvelden
            settings.CompanyName = dto.CompanyName ?? settings.CompanyName;
            settings.Address = dto.Address;
            settings.Email = dto.Email;
            settings.Phone = dto.Phone;
            settings.KvkNumber = dto.KvkNumber;
            settings.VatNumber = dto.VatNumber;
            settings.Iban = dto.Iban;

            // Logo Upload Magie!
            if (dto.LogoFile != null)
            {
                // Maak een 'logos' mapje aan in je backend folder
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "logos");
                Directory.CreateDirectory(uploadsFolder);
                
                // Geef het bestand een unieke naam (zodat logo.png niet per ongeluk een andere logo.png overschrijft)
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + dto.LogoFile.FileName;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.LogoFile.CopyToAsync(fileStream);
                }
                
                // Sla het pad op in de database zodat de PDF hem straks kan vinden
                settings.LogoPath = Path.Combine("wwwroot", "logos", uniqueFileName);
            }

            await _context.SaveChangesAsync();
            return Ok(settings);
        }
    }

    // Dit vertelt C# precies welke data er vanuit het React formulier binnenkomt
    public class CompanySettingsDto
    {
        public string? CompanyName { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? KvkNumber { get; set; }
        public string? VatNumber { get; set; }
        public string? Iban { get; set; }
        public IFormFile? LogoFile { get; set; } // Dit is het daadwerkelijke plaatje!
    }
}