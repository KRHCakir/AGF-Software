using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in PartijService. Partijen lezen en aanmaken mag met Inkoop of Voorraad; de nummering instellen
    // staat bij de bedrijfsinstellingen (SettingsController, Beheer).
    [Authorize(Policy = Permissions.PurchaseOrStock)]
    [ApiController]
    [Route("api/partijen")]
    public class PartijenController : ControllerBase
    {
        private readonly PartijService _service;

        public PartijenController(PartijService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string query, [FromQuery] string status, [FromQuery] string kind, [FromQuery] int page = 1, [FromQuery] int pageSize = PartijService.DefaultPageSize,
            [FromQuery] string sort = null, [FromQuery] string direction = null)
        {
            if (!this.TryReadSort(sort, direction, ListSortFields.Partijen, out var order, out var rejected)) return rejected;
            return this.ToActionResult(await _service.ListAsync(query, status, page, pageSize, HttpContext?.RequestAborted ?? default, kind, order));
        }

        // Werklijst "Partij ontbreekt": voorraad zonder partij (alleen van vóór regel 41, bijvoorbeeld in een kopie). Alleen tonen, niets verzinnen.
        [HttpGet("missing")]
        public async Task<IActionResult> Missing() =>
            Ok(await _service.MissingAsync(HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpGet("next-number")]
        public async Task<IActionResult> NextNumber([FromQuery] string format, [FromQuery] string reset, [FromQuery] string kind, [FromQuery] int? digits) =>
            this.ToActionResult(await _service.NextNumberAsync(format, reset, kind, digits, HttpContext?.RequestAborted ?? default));

        // Dubbelcontrole: de partij van deze soort (standaard Inkoop) met dezelfde sleutel (hoofdletters, spaties en streepjes tellen
        // niet), of 204 als die er niet is.
        [HttpGet("by-number")]
        public async Task<IActionResult> ByNumber([FromQuery] string number, [FromQuery] string kind)
        {
            var partij = await _service.FindByNumberAsync(number, string.IsNullOrWhiteSpace(kind) ? PartijRules.KindPurchase : kind.Trim(), HttpContext?.RequestAborted ?? default);
            return partij == null ? NoContent() : Ok(partij);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePartijDto request) =>
            this.ToActionResult(await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/correct-number")]
        public async Task<IActionResult> CorrectNumber(int id, [FromBody] CorrectPartijNumberDto request) =>
            this.ToActionResult(await _service.CorrectNumberAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Een inkooporder (kop en regels) aan deze partij koppelen, ook vanuit de ontvangst; na ontvangst alleen een order zonder
        // partij, met reden.
        [HttpPost("{id:int}/orders")]
        public async Task<IActionResult> LinkOrder(int id, [FromBody] LinkPartijDto request) =>
            this.ToActionResult(await _service.LinkOrderAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Feedback 22 november: de kleur van een inkoop-partij (bij de keuring of op het partijdetail), met historie.
        [HttpPut("{id:int}/color")]
        public async Task<IActionResult> SetColor(int id, [FromBody] SetPartijColorDto request, [FromServices] LotColorService colors) =>
            this.ToActionResult(await colors.SetPartijColorAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _service.DeleteAsync(id, HttpContext?.RequestAborted ?? default);
            return result.Status == ServiceResultStatus.Ok ? NoContent() : this.ToActionResult(result);
        }
    }
}
