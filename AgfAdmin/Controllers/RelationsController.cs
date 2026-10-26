using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Relatiewerkplek: klanten/debiteuren en leveranciers/crediteuren als één onderneming met rollen. Dunne controller op RelationService.
    [Route("api/relations")]
    [ApiController]
    public class RelationsController : ControllerBase
    {
        private readonly RelationService _service;

        public RelationsController(RelationService service) => _service = service;

        // role: all (standaard), customer, vendor. query: code, naam, handelsnaam, btw, e-mail, telefoon, plaats, contact. Begrensd (paginering).
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string role, [FromQuery] string query, [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = RelationRules.DefaultPageSize) =>
            Ok(await _service.ListAsync(role, query, includeInactive, page, pageSize, HttpContext?.RequestAborted ?? default));

        [HttpGet("reference")]
        public async Task<IActionResult> Reference() => Ok(await _service.ReferenceAsync(HttpContext?.RequestAborted ?? default));

        // Bedrijfsinstelling: link naar de GLOBALG.A.P. Supply Chain Portal (absolute https-URL).
        [HttpGet("settings")]
        public async Task<IActionResult> Settings() => Ok(await _service.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] RelationSettingsDto request) =>
            this.ToActionResult(await _service.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        // 201 nieuw, 200 identieke herhaling (requestId), 400 validatie, 409 gelijkende naam zonder bevestiging / afwijkende herhaling / codeconflict.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveRelationDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
        }

        // Bijwerken met de verwachte versie (409 bij verouderde versie of codeconflict). Een bestaande rol wordt nooit verwijderd.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveRelationDto request) =>
            this.ToActionResult(await _service.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Bankrekening bevestigen (wie + verwachte versie): Unconfirmed -> Confirmed met auditregel; 409 bij verouderde versie.
        [HttpPost("{id:int}/bank-accounts/{bankAccountId:int}/confirm")]
        public async Task<IActionResult> ConfirmBankAccount(int id, int bankAccountId, [FromBody] ConfirmBankAccountDto request) =>
            this.ToActionResult(await _service.ConfirmBankAccountAsync(id, bankAccountId, request, HttpContext?.RequestAborted ?? default));

        // Tweede rol toevoegen zonder tweede onderneming: role = customer | vendor. 201 nieuw, 200 als de rol al bestond.
        [HttpPost("{id:int}/roles/{role}")]
        public async Task<IActionResult> AddRole(int id, string role)
        {
            var result = await _service.AddRoleAsync(id, role, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
        }
    }
}
