using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in PurchaseOrderService. Aanmaken blijft POST /api/supply-commitments (Aanvoer plannen).
    [ApiController]
    [Route("api/purchase-orders")]
    public class PurchaseOrdersController : ControllerBase
    {
        private readonly PurchaseOrderService _service;

        public PurchaseOrdersController(PurchaseOrderService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string query, [FromQuery] string status, [FromQuery] int? vendorId,
            [FromQuery] int page = 1, [FromQuery] int pageSize = PurchaseOrderRules.DefaultPageSize) =>
            this.ToActionResult(await _service.ListAsync(query, status, vendorId, page, pageSize, HttpContext?.RequestAborted ?? default));

        [HttpGet("by-invoice-number")]
        public async Task<IActionResult> ByInvoiceNumber([FromQuery] string invoiceNumber, [FromQuery] int? vendorId) =>
            Ok(await _service.FindByInvoiceNumberAsync(invoiceNumber, vendorId, HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/invoice-refs")]
        public async Task<IActionResult> AddInvoiceRef(int id, [FromBody] AddPurchaseOrderInvoiceRefDto request) =>
            this.ToActionResult(await _service.AddInvoiceRefAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
