using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Alleen lezen: overzicht en drill-down van beschikbaarheid. Geen verkoop, reservering of voorraadmutatie.
    [ApiController]
    [Route("api/availability")]
    public class AvailabilityController : ControllerBase
    {
        private readonly IAvailabilityService _service;

        public AvailabilityController(IAvailabilityService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetOverview(
            [FromQuery] int? productVariantId,
            [FromQuery] string unit,
            [FromQuery] string route,
            [FromQuery] int? customerId)
        {
            var query = new AvailabilityQuery { ProductVariantId = productVariantId, Unit = unit, Route = route, CustomerId = customerId };
            return this.ToActionResult(await _service.GetOverviewAsync(query, HttpContext?.RequestAborted ?? default));
        }

        [HttpGet("variants/{productVariantId:int}")]
        public async Task<IActionResult> GetVariant(
            int productVariantId,
            [FromQuery] int? customerId,
            [FromQuery] string route,
            [FromQuery] string unit,
            [FromQuery] string lot,
            [FromQuery] string category,
            [FromQuery] string qualityStatus,
            [FromQuery] bool includeClosed = false)
        {
            var query = new AvailabilityQuery
            {
                CustomerId = customerId,
                Route = route,
                Unit = unit,
                Lot = lot,
                Category = category,
                QualityStatus = qualityStatus,
                IncludeClosed = includeClosed
            };
            return this.ToActionResult(await _service.GetVariantAsync(productVariantId, query, HttpContext?.RequestAborted ?? default));
        }
    }
}
