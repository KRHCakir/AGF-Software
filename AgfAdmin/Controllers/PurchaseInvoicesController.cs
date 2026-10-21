using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Nieuwe invoerwerkplek voor inkoopfacturen. Financieel én administratief: raakt nooit voorraad, pallets of kwaliteit.
    [Route("api/purchase-invoices")]
    [ApiController]
    public class PurchaseInvoicesController : ControllerBase
    {
        private readonly PurchaseInvoiceService _service;

        public PurchaseInvoicesController(PurchaseInvoiceService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List() => Ok(await _service.ListAsync(HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseInvoiceDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
        }

        [HttpGet("vendors")]
        public async Task<IActionResult> Vendors() => Ok(await _service.VendorsAsync(HttpContext?.RequestAborted ?? default));

        [HttpGet("settings")]
        public async Task<IActionResult> Settings() => this.ToActionResult(await _service.GetSettingsAsync(HttpContext?.RequestAborted ?? default));

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] UpdatePurchaseInvoiceSettingsDto request) =>
            this.ToActionResult(await _service.UpdateSettingsAsync(request, HttpContext?.RequestAborted ?? default));
    }
}
