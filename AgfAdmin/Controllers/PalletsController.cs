using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in IPalletService. Wijzigingen gaan via expliciete commando's, nooit via PUT/PATCH.
    [ApiController]
    [Route("api/pallets")]
    public class PalletsController : ControllerBase
    {
        private readonly IPalletService _service;

        public PalletsController(IPalletService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePalletDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpPost("{id:int}/content-lines")]
        public async Task<IActionResult> AddContentLine(int id, [FromBody] AddPalletContentLineDto request)
        {
            var result = await _service.AddContentLineAsync(id, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id = created.PalletId }, created));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/logistics-status")]
        public async Task<IActionResult> ChangeLogisticsStatus(int id, [FromBody] ChangeLogisticsStatusDto request) =>
            this.ToActionResult(await _service.ChangeLogisticsStatusAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/location")]
        public async Task<IActionResult> ChangeLocation(int id, [FromBody] ChangePalletLocationDto request) =>
            this.ToActionResult(await _service.ChangeLocationAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/content-lines/{lineId:int}/quality-status")]
        public async Task<IActionResult> ChangeQualityStatus(int id, int lineId, [FromBody] ChangeQualityStatusDto request) =>
            this.ToActionResult(await _service.ChangeQualityStatusAsync(id, lineId, request, HttpContext?.RequestAborted ?? default));

        // Alleen lezen: auditrecords worden nooit via de API gewijzigd of verwijderd.
        [HttpGet("{id:int}/audit-events")]
        public async Task<IActionResult> GetAuditEvents(int id) =>
            this.ToActionResult(await _service.GetAuditEventsAsync(id, HttpContext?.RequestAborted ?? default));
    }
}
