using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Transport op een verkooporder (documenten deel 2). Onder Verkoop, zoals de order en haar documenten. Na een geslaagde PUT ververst het
    // filter de documenten: een bevestigde order krijgt zo de transportopdracht of een nieuwe versie.
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/sales-orders/{id:int}/transport")]
    [RefreshLoadingSlip]
    public class SalesOrderTransportController(SalesOrderTransportService transports) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await transports.GetAsync(id, HttpContext?.RequestAborted ?? default));

        // 200 met het transport; 400 bij ongeldige invoer; 404; 409 geannuleerd of intussen gewijzigd.
        [HttpPut]
        public async Task<IActionResult> Save(int id, [FromBody] SaveSalesOrderTransportDto request) =>
            this.ToActionResult(await transports.SaveAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Alleen zolang er geen transportopdracht is (409 anders).
        [HttpDelete]
        public async Task<IActionResult> Delete(int id) => this.ToActionResult(await transports.DeleteAsync(id, HttpContext?.RequestAborted ?? default));
    }
}
