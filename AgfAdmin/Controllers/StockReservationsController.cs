using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/stock-reservations")]
    public class StockReservationsController : ControllerBase
    {
        private readonly IStockReservationService _reservations;

        public StockReservationsController(IStockReservationService reservations)
        {
            _reservations = reservations;
        }

        // Vrijgeven met een verplichte reden en de verwachte reserveringsversie (optimistische concurrency).
        [HttpPost("{id:int}/release")]
        public async Task<IActionResult> Release(int id, [FromBody] ReleaseStockReservationDto request) =>
            this.ToActionResult(await _reservations.ReleaseAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
