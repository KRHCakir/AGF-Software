using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // De ECB-koers voor een valuta en factuurdatum, om te tonen in het invoerscherm van een inkoopfactuur. Slaat niets op; de koers
    // van een opgeslagen factuur haalt de server zelf op (POST api/purchase-invoices/{id}/exchange-rate).
    [Route("api/exchange-rates")]
    [Authorize(Policy = Permissions.Purchase)]
    [ApiController]
    public class ExchangeRatesController : ControllerBase
    {
        private readonly InvoiceExchangeRateService _rates;

        public ExchangeRatesController(InvoiceExchangeRateService rates)
        {
            _rates = rates;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string currency, [FromQuery] DateTime? date) =>
            this.ToActionResult(await _rates.QuoteAsync(currency, date, HttpContext?.RequestAborted ?? default));
    }
}
