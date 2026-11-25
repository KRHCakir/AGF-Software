using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Verkoopfactuur 1: verkoopfacturen onder Verkoop. Lezen en wat er nog te factureren is; het concept maken en bijwerken volgt.
    // De oude route /api/SalesInvoice blijft voor de oude facturen.
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/sales-invoices")]
    public class SalesInvoicesController(SalesInvoiceService invoices) : ControllerBase
    {
        private CancellationToken Aborted => HttpContext?.RequestAborted ?? default;

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await invoices.GetAsync(id, Aborted));

        // De werklijst Nog te factureren: leveringen met een rest, per klant.
        [HttpGet("to-invoice")]
        public async Task<IActionResult> ToInvoice() => Ok(await invoices.ToInvoiceAsync(Aborted));

        // Per levering van een verkooporder: geleverd, gefactureerd en rest, met de facturen.
        [HttpGet("/api/sales-orders/{orderId:int}/invoicing")]
        public async Task<IActionResult> OrderInvoicing(int orderId) => this.ToActionResult(await invoices.OrderInvoicingAsync(orderId, Aborted));
    }
}
