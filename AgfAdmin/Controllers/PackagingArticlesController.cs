using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Emballage-artikelen (fust: Pallet wegwerp, Euro-pallet, Krat): stamgegevens, dus lezen mag iedereen (de verkooporder kiest eruit),
    // wijzigen vraagt Stamgegevens. Zelfde tabel als de kostenartikelen (soort Packaging), daarom ook kiesbaar op een inkoopfactuur.
    [Authorize]
    [ApiController]
    [Route("api/packaging-articles")]
    public class PackagingArticlesController : ControllerBase
    {
        private readonly CostArticleService _service;

        public PackagingArticlesController(CostArticleService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
            Ok(await _service.ListAsync(includeInactive, HttpContext?.RequestAborted ?? default, CostArticleRules.KindPackaging));

        [Authorize(Policy = Permissions.MasterDataWrites)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SavePackagingArticleDto request) =>
            this.ToActionResult(await _service.CreatePackagingAsync(request, HttpContext?.RequestAborted ?? default));

        [Authorize(Policy = Permissions.MasterDataWrites)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SavePackagingArticleDto request) =>
            this.ToActionResult(await _service.UpdatePackagingAsync(id, request, HttpContext?.RequestAborted ?? default));

        [Authorize(Policy = Permissions.MasterDataWrites)]
        [HttpPut("{id:int}/active")]
        public async Task<IActionResult> SetActive(int id, [FromBody] SetCostArticleActiveDto request) =>
            this.ToActionResult(await _service.SetActiveAsync(id, request, HttpContext?.RequestAborted ?? default, CostArticleRules.KindPackaging));
    }
}
