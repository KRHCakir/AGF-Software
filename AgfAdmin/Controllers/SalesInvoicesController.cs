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

        // De lijst: ?query=&status=all|open|Concept|Final|Discarded&sort=&direction=&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string query, [FromQuery] string status, [FromQuery] string sort, [FromQuery] string direction,
            [FromQuery] int page = 1, [FromQuery] int pageSize = SalesInvoiceService.DefaultPageSize) =>
            this.ToActionResult(await invoices.ListAsync(query, status, sort, direction, page, pageSize, Aborted));

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

        // Definitief maken: nummer, momentopname en (directe factuur) afboeken. 200 ook bij herhaling; 400 met wat er ontbreekt; 409.
        [HttpPost("{id:int}/finalize")]
        public async Task<IActionResult> Finalize(int id, [FromBody] FinalizeSalesInvoiceDto request) =>
            this.ToActionResult(await invoices.FinalizeAsync(id, request, Aborted));

        // Een selectie: elke factuur apart; het antwoord zegt per factuur of ze definitief is en zo niet, waarom.
        [HttpPost("finalize")]
        public async Task<IActionResult> FinalizeMany([FromBody] FinalizeSalesInvoicesDto request) =>
            this.ToActionResult(await invoices.FinalizeManyAsync(request, Aborted));

        // De PDF: een concept live (met CONCEPT), een definitieve factuur uit haar bewaarde versie. Inline, of met download=true als bestand.
        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> Pdf(int id, [FromQuery] bool download = false)
        {
            var result = await invoices.PdfAsync(id, Aborted);
            if (result.Status != ServiceResultStatus.Ok) return this.ToActionResult(result);
            return download ? File(result.Value.Bytes, "application/pdf", result.Value.FileName) : File(result.Value.Bytes, "application/pdf");
        }

        [HttpGet("{id:int}/documents")]
        public async Task<IActionResult> Documents(int id) => this.ToActionResult(await invoices.DocumentsAsync(id, Aborted));

        // De werklijst Nog te factureren: leveringen met een rest, per klant.
        [HttpGet("to-invoice")]
        public async Task<IActionResult> ToInvoice() => Ok(await invoices.ToInvoiceAsync(Aborted));

        // Per levering van een verkooporder: geleverd, gefactureerd en rest, met de facturen.
        [HttpGet("/api/sales-orders/{orderId:int}/invoicing")]
        public async Task<IActionResult> OrderInvoicing(int orderId) => this.ToActionResult(await invoices.OrderInvoicingAsync(orderId, Aborted));
    }
}
