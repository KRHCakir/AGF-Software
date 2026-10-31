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
        private readonly ArticleLifecycleService _lifecycle;
        private readonly ArticleFromEntryService _fromEntry;
        private readonly GoodsCodeCatalog _goodsCodes;

        public ArticlesController(ArticleService service, ArticleLifecycleService lifecycle, ArticleFromEntryService fromEntry, GoodsCodeCatalog goodsCodes)
        {
            _service = service;
            _lifecycle = lifecycle;
            _fromEntry = fromEntry;
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

        [HttpGet("origin-cleanup")]
        public async Task<IActionResult> OriginCleanup() => Ok(await _service.OriginCleanupAsync(HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/origin-country")]
        public async Task<IActionResult> SetOriginCountry(int id, [FromBody] SetOriginCountryDto request) =>
            this.ToActionResult(await _service.SetOriginCountryAsync(id, request, HttpContext?.RequestAborted ?? default));

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

        // "Artikel aanmaken en doorgaan" vanuit aanvoer of ontvangst: 201 nieuw, 200 herhaling (zelfde requestId), 409 "article-exists"
        // met het bestaande artikel, 409 "article-request-reused" als de requestId al voor iets anders werd gebruikt.
        [HttpPost("from-entry")]
        public async Task<IActionResult> CreateFromEntry([FromBody] CreateArticleFromEntryDto request) =>
            this.ToActionResult(await _fromEntry.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        // Waar het artikel aan hangt; canDelete = false met de reden in gewone taal.
        [HttpGet("{id:int}/usage")]
        public async Task<IActionResult> Usage(int id) => this.ToActionResult(await _lifecycle.UsageAsync(id, HttpContext?.RequestAborted ?? default));

        // 204 verwijderd; 409 { message, code: "article-in-use", usages } als er iets aan hangt (dan archiveren).
        [HttpPost("{id:int}/delete")]
        public async Task<IActionResult> Delete(int id, [FromBody] ArticleActionDto request)
        {
            var result = await _lifecycle.DeleteAsync(id, request, HttpContext?.RequestAborted ?? default);
            return result.Status == ServiceResultStatus.Ok ? NoContent() : this.ToActionResult(result);
        }

        [HttpPost("{id:int}/archive")]
        public async Task<IActionResult> Archive(int id, [FromBody] ArticleActionDto request) =>
            this.ToActionResult(await _lifecycle.ArchiveAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/restore")]
        public async Task<IActionResult> Restore(int id, [FromBody] ArticleActionDto request) =>
            this.ToActionResult(await _lifecycle.RestoreAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Productsoorten zonder artikelen, en zo'n soort verwijderen (409 "product-has-articles" zolang er artikelen onder hangen).
        [HttpGet("products/empty")]
        public async Task<IActionResult> EmptyProducts() => Ok(await _lifecycle.EmptyProductsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPost("products/{productId:int}/delete")]
        public async Task<IActionResult> DeleteProduct(int productId, [FromBody] ArticleActionDto request)
        {
            var result = await _lifecycle.DeleteProductAsync(productId, request, HttpContext?.RequestAborted ?? default);
            return result.Status == ServiceResultStatus.Ok ? NoContent() : this.ToActionResult(result);
        }
    }
}
