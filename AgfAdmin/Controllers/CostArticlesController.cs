using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Kostenartikelen (type Dienst) voor factuurregels van derden. Kosten en facturen vallen onder Inkoop.
    [Authorize(Policy = Permissions.Purchase)]
    [ApiController]
    [Route("api/cost-articles")]
    public class CostArticlesController : ControllerBase
    {
        private readonly CostArticleService _service;

        public CostArticlesController(CostArticleService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
            Ok(await _service.ListAsync(includeInactive, HttpContext?.RequestAborted ?? default));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCostArticleDto request) =>
            this.ToActionResult(await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpPut("{id:int}/active")]
        public async Task<IActionResult> SetActive(int id, [FromBody] SetCostArticleActiveDto request) =>
            this.ToActionResult(await _service.SetActiveAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
