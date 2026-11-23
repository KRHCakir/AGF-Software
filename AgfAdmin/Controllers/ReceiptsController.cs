using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Ontvangen in één scherm (feedback 22 november). Dunne laag: alle regels zitten in ReceiptService; de actor komt uit CurrentUser.
    [Authorize(Policy = Permissions.Stock)]
    [ApiController]
    [Route("api/receipts")]
    public class ReceiptsController : ControllerBase
    {
        private readonly ReceiptService _service;

        public ReceiptsController(ReceiptService service)
        {
            _service = service;
        }

        // Dit wordt verwacht: per regel besteld, ontvangen en nog te ontvangen, met de GGN-melding als regel.
        [HttpGet("orders/{orderId:int}")]
        public async Task<IActionResult> Get(int orderId) =>
            this.ToActionResult(await _service.GetAsync(orderId, HttpContext?.RequestAborted ?? default));

        // Eén klik Ontvangen voor een regel (met een afwijkend aantal en optioneel palletnummers).
        [HttpPost("orders/{orderId:int}/lines/{lineId:int}")]
        public async Task<IActionResult> Receive(int orderId, int lineId, [FromBody] ReceiveLineDto request) =>
            this.ToActionResult(await _service.ReceiveLineAsync(orderId, lineId, request, HttpContext?.RequestAborted ?? default));
    }
}
