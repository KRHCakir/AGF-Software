using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Betalingscondities (stamgegevens). Dunne controller op PaymentConditionService.
    [Route("api/payment-conditions")]
    [ApiController]
    public class PaymentConditionsController : ControllerBase
    {
        private readonly PaymentConditionService _service;

        public PaymentConditionsController(PaymentConditionService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
            Ok(await _service.ListAsync(includeInactive, HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        // 201 nieuw, 200 identieke herhaling (requestId), 400 validatie (ook bezette code), 409 afwijkende herhaling.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SavePaymentConditionDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
        }

        // Bijwerken met verwachte versie (409 bij verouderde versie).
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SavePaymentConditionDto request) =>
            this.ToActionResult(await _service.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));

        // 204 verwijderd; 409 { message, code: "payment-condition-in-use", customerCount, vendorCount } als een rol haar gebruikt.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _service.DeleteAsync(id, HttpContext?.RequestAborted ?? default);
            return result.Status == ServiceResultStatus.Ok ? NoContent() : this.ToActionResult(result);
        }
    }
}
