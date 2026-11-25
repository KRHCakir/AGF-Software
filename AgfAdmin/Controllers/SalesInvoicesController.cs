using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Verkoopfactuur 1: verkoopfacturen onder Verkoop. Concept uit leveringen, bijwerken, weggooien, lezen en wat er nog te factureren is.
    // De oude route /api/SalesInvoice blijft voor de oude facturen.
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/sales-invoices")]
    public class SalesInvoicesController(SalesInvoiceService invoices) : ControllerBase
    {
        private CancellationToken Aborted => HttpContext?.RequestAborted ?? default;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await invoices.GetAsync(id, Aborted));

        // 201 met het concept, 200 bij herhaling van dezelfde aanvraag, 400 (andere klant), 404, 409 (niets meer te factureren).
        [HttpPost("from-deliveries")]
        public async Task<IActionResult> FromDeliveries([FromBody] CreateInvoiceFromDeliveriesDto request) =>
            this.ToActionResult(await invoices.CreateFromDeliveriesAsync(request, Aborted));

        // Directe factuur (zonder order): 201 met het concept en een eigen V-nummer, 200 bij herhaling, 400 (geen partij, meer dan vrij), 409.
        [HttpPost]
        public async Task<IActionResult> CreateDirect([FromBody] CreateDirectInvoiceDto request) =>
            this.ToActionResult(await invoices.CreateDirectAsync(request, Aborted));

        [HttpPost("{id:int}/deliveries")]
        public async Task<IActionResult> AddDeliveries(int id, [FromBody] AddInvoiceDeliveriesDto request) =>
            this.ToActionResult(await invoices.AddDeliveriesAsync(id, request, Aborted));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateSalesInvoiceDto request) =>
            this.ToActionResult(await invoices.UpdateAsync(id, request, Aborted));

        [HttpPost("{id:int}/discard")]
        public async Task<IActionResult> Discard(int id, [FromBody] DiscardSalesInvoiceDto request) =>
            this.ToActionResult(await invoices.DiscardAsync(id, request, Aborted));

        // De werklijst Nog te factureren: leveringen met een rest, per klant.
        [HttpGet("to-invoice")]
        public async Task<IActionResult> ToInvoice() => Ok(await invoices.ToInvoiceAsync(Aborted));

        // Per levering van een verkooporder: geleverd, gefactureerd en rest, met de facturen.
        [HttpGet("/api/sales-orders/{orderId:int}/invoicing")]
        public async Task<IActionResult> OrderInvoicing(int orderId) => this.ToActionResult(await invoices.OrderInvoicingAsync(orderId, Aborted));
    }
}
