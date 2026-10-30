using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Alleen lezen: kiezen van het werkelijk ontvangen artikel en wat "Ja, klopt" zou doen. Boeken gaat via POST /api/pallets/{id}/content-lines.
    [ApiController]
    [Route("api/supply-commitment-lines/{lineId:int}")]
    public class ReceiptDeviationsController : ControllerBase
    {
        private readonly ReceiptDeviationService _service;

        public ReceiptDeviationsController(ReceiptDeviationService service)
        {
            _service = service;
        }

        [HttpGet("deviation-candidates")]
        public async Task<IActionResult> Candidates(int lineId) =>
            this.ToActionResult(await _service.CandidatesAsync(lineId, HttpContext?.RequestAborted ?? default));

        [HttpGet("deviation-preview")]
        public async Task<IActionResult> Preview(int lineId, [FromQuery] int? receivedProductVariantId, [FromQuery] decimal? quantity) =>
            this.ToActionResult(await _service.PreviewAsync(lineId, receivedProductVariantId, quantity, HttpContext?.RequestAborted ?? default));
    }
}
