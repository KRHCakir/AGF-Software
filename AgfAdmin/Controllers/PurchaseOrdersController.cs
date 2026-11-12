using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in PurchaseOrderService. Aanmaken blijft POST /api/supply-commitments (Aanvoer plannen).
    [Authorize(Policy = Permissions.Purchase)]
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
            [FromQuery] int page = 1, [FromQuery] int pageSize = PurchaseOrderRules.DefaultPageSize, [FromQuery] string supplierReference = null,
            [FromQuery] string document = null) =>
            this.ToActionResult(await _service.ListAsync(query, status, vendorId, page, pageSize, supplierReference, HttpContext?.RequestAborted ?? default, document));

        [HttpGet("by-supplier-reference")]
        public async Task<IActionResult> BySupplierReference([FromQuery] string supplierReference, [FromQuery] int vendorId) =>
            Ok(await _service.FindBySupplierReferenceAsync(supplierReference, vendorId, HttpContext?.RequestAborted ?? default));

        [HttpGet("by-invoice-number")]
        public async Task<IActionResult> ByInvoiceNumber([FromQuery] string invoiceNumber, [FromQuery] int? vendorId) =>
            Ok(await _service.FindByInvoiceNumberAsync(invoiceNumber, vendorId, HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        // Aanpassen zolang de order Open is (stap 4a): 200 met de order, 400 validatie, 409 definitief, verouderde versie of niet aanpasbaar.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseOrderDto request) =>
            this.ToActionResult(await _service.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/invoice-refs")]
        public async Task<IActionResult> AddInvoiceRef(int id, [FromBody] AddPurchaseOrderInvoiceRefDto request) =>
            this.ToActionResult(await _service.AddInvoiceRefAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/rejections")]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectPurchaseOrderDto request) =>
            this.ToActionResult(await _service.RejectAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
