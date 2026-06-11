using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SettingsController(AppDbContext context)
        {
            _context = context;
        }

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