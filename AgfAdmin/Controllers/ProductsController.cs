using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgfAdmin.DTOs;

namespace AgfAdmin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Products?includeVariants=true
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts([FromQuery] bool includeVariants = false)
        {
            if (includeVariants)
            {
                return await _context.Products.Include(p => p.Variants).ToListAsync();
            }
            return await _context.Products.ToListAsync();
        }

        // GET: api/Products/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            return product;
        }

        // PUT: api/Products/variants/5/shortcut
        [HttpPut("variants/{id}/shortcut")]
        public async Task<IActionResult> UpdateShortcut(int id, [FromBody] UpdateShortcutDto dto)
        {
            var variant = await _context.ProductVariants.FindAsync(id);
            if (variant == null) return NotFound();

            // Clear shortcut if empty
            if (string.IsNullOrWhiteSpace(dto.ShortcutCode))
            {
                variant.ShortcutCode = null;
            }
            else
            {
                // Ensure no duplicates
                var existing = await _context.ProductVariants.FirstOrDefaultAsync(v => v.ShortcutCode != null && v.ShortcutCode.ToLower() == dto.ShortcutCode.ToLower() && v.Id != id);
                if (existing != null)
                {
                    return BadRequest(new { Message = $"Shortcut '{dto.ShortcutCode}' is already used by variant ID {existing.Id}" });
                }
                variant.ShortcutCode = dto.ShortcutCode.Trim();
            }

            await _context.SaveChangesAsync();
            return Ok(new { Message = "Shortcut updated successfully" });
        }

        [HttpPost("quick-shortcut")]
public async Task<IActionResult> AddQuickShortcut([FromBody] QuickAddShortcutDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.ProductName))
        return BadRequest("Productnaam is verplicht.");

    var pName = dto.ProductName.Trim().ToLower();
    
    // 1. Zoek of maak het Hoofdproduct
    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.Name.ToLower() == pName);

    if (product == null)
    {
        product = new Product { Name = dto.ProductName.Trim() };
        _context.Products.Add(product);
        await _context.SaveChangesAsync(); // Sla op om het ProductId te genereren
    }

    // 2. Definieer de variant-velden veilig
    var vName = dto.VariantName?.Trim() ?? "";
    var vOrigin = dto.Origin?.Trim() ?? "";
    var vSize = dto.SizeCategory?.Trim() ?? "";

    // 3. Zoek of maak de specifieke Variant
    var variant = await _context.ProductVariants
        .FirstOrDefaultAsync(v => 
            v.ProductId == product.Id &&
            (v.Name ?? "") == vName &&
            (v.Origin ?? "") == vOrigin &&
            (v.SizeCategory ?? "") == vSize);

    if (variant == null)
    {
        variant = new ProductVariant
        {
            ProductId = product.Id,
            Name = dto.VariantName?.Trim() ?? "",
            Origin = dto.Origin?.Trim() ?? "",
            SizeCategory = dto.SizeCategory?.Trim() ?? "",
            Season = dto.Season?.Trim() ?? "", // <-- DEZE TOEVOEGEN
            Notes = dto.Notes?.Trim() ?? "",   // <-- DEZE TOEVOEGEN
            Packaging = "Default",
            DefaultPrice = 0
        };
        _context.ProductVariants.Add(variant);
    }

    // 4. Update de ShortcutCode
    variant.ShortcutCode = dto.ShortcutCode.Trim().ToLower();
    
    await _context.SaveChangesAsync();

    return Ok(new { Message = "Shortcut succesvol gekoppeld!", VariantId = variant.Id });
}
    }

    public class UpdateShortcutDto
    {
        public string? ShortcutCode { get; set; }
    }
}
