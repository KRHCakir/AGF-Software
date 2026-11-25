using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Verkoopfactuur 1: omzetsoorten. Lezen mag iedereen (de keuzelijst op de factuur), wijzigen alleen met Beheer.
    [Authorize(Policy = Permissions.AdminWrites)]
    [ApiController]
    [Route("api/revenue-types")]
    public class RevenueTypesController(RevenueTypeService types) : ControllerBase
    {
        private CancellationToken Aborted => HttpContext?.RequestAborted ?? default;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false) => Ok(await types.ListAsync(includeInactive, Aborted));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveRevenueTypeDto request) => this.ToActionResult(await types.CreateAsync(request, Aborted));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveRevenueTypeDto request) => this.ToActionResult(await types.UpdateAsync(id, request, Aborted));
    }
}
