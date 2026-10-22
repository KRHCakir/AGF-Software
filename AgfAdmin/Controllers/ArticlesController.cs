using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Artikelwerkplek: beheer van Products/ProductVariants met duidelijke labels. De bestaande api/products blijft bestaan.
    [Route("api/articles")]
    [ApiController]
    public class ArticlesController : ControllerBase
    {
        private readonly ArticleService _service;
        private readonly GoodsCodeCatalog _goodsCodes;

        public ArticlesController(ArticleService service, GoodsCodeCatalog goodsCodes)
        {
            _service = service;
            _goodsCodes = goodsCodes;
        }

        [HttpGet]
        public async Task<IActionResult> List() => Ok(await _service.ListAsync(HttpContext?.RequestAborted ?? default));

        [HttpGet("reference")]
        public async Task<IActionResult> Reference() => Ok(await _service.ReferenceAsync(HttpContext?.RequestAborted ?? default));

        // Lokale referentielijst (herkomst en versie in het antwoord); nooit een volledige catalogus, nooit een automatische keuze.
        // Het antwoord bevat uitsluitend codes van het gekozen stelsel (HS 6, CN 8, TARIC 10); zonder stelsel geldt CN/GN.
        [HttpGet("goods-codes")]
        public IActionResult GoodsCodes([FromQuery] string query, [FromQuery] string system)
        {
            var chosen = string.IsNullOrWhiteSpace(system) ? ArticleRules.SystemCn : system.Trim().ToUpperInvariant();
            if (!ArticleRules.IsGoodsCodeSystem(chosen))
            {
                return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["system"] = new[] { $"Code system must be one of: {string.Join(", ", ArticleRules.GoodsCodeSystems)}." }
                }));
            }
            return Ok(_goodsCodes.Result(query, chosen));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveArticleDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveArticleDto request) =>
            this.ToActionResult(await _service.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
