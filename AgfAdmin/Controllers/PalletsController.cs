using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in IPalletService. Status-, locatie- en kwaliteitswijzigingen volgen apart.
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
    }
}
