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

        [HttpGet("next-number")]
        public async Task<IActionResult> NextNumber([FromQuery] string format, [FromQuery] string reset) =>
            this.ToActionResult(await _service.NextNumberAsync(format, reset, HttpContext?.RequestAborted ?? default));

        // Dubbelcontrole: de partij met dezelfde sleutel (hoofdletters, spaties en streepjes tellen niet), of 204 als die er niet is.
        [HttpGet("by-number")]
        public async Task<IActionResult> ByNumber([FromQuery] string number)
        {
            var partij = await _service.FindByNumberAsync(number, HttpContext?.RequestAborted ?? default);
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

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _service.DeleteAsync(id, HttpContext?.RequestAborted ?? default);
            return result.Status == ServiceResultStatus.Ok ? NoContent() : this.ToActionResult(result);
        }
    }
}
