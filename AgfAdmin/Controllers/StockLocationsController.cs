using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Voorraadlocaties (feedback 22 november): stamgegevens. Lezen mag iedereen met stamgegevens (ook inkoop kiest er een op de order),
    // wijzigen volgt het beleid van de stamgegevens. Dunne laag: de regels zitten in StockLocationService.
    [Authorize(Policy = Permissions.MasterDataWrites)]
    [ApiController]
    [Route("api/stock-locations")]
    public class StockLocationsController : ControllerBase
    {
        private readonly StockLocationService _service;

        public StockLocationsController(StockLocationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List() => Ok(await _service.ListAsync(HttpContext?.RequestAborted ?? default));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveStockLocationDto request) =>
            this.ToActionResult(await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        // Ontvangstlocatie van een nieuwe inkooporder: de standaard en het adres van de eigen loods (november 2026).
        [HttpGet("receive-defaults")]
        public async Task<IActionResult> ReceiveDefaults() => Ok(await _service.ReceiveDefaultsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("receive-defaults")]
        public async Task<IActionResult> SaveReceiveDefaults([FromBody] SaveReceiveDefaultsDto request) =>
            this.ToActionResult(await _service.SaveReceiveDefaultsAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveStockLocationDto request) =>
            this.ToActionResult(await _service.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
