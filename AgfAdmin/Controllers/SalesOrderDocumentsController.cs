using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgfAdmin.Controllers
{
    // Bevestigen van een verkooporder en haar documenten (documenten deel 1: de laadbon). Onder Verkoop, zoals de order zelf.
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/sales-orders/{id:int}")]
    public class SalesOrderDocumentsController(LoadingSlipService slips) : ControllerBase
    {
        // 200 met de documenten (ook als de order al bevestigd was), 400 zonder regels, 404, 409 geannuleerd.
        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmSalesOrderDto request) =>
            this.ToActionResult(await slips.ConfirmAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Laden op en kenteken na bevestigen; een andere bon geeft een nieuwe versie.
        [HttpPut("loading")]
        public async Task<IActionResult> Loading(int id, [FromBody] UpdateSalesOrderLoadingDto request) =>
            this.ToActionResult(await slips.UpdateLoadingAsync(id, request, HttpContext?.RequestAborted ?? default));

        [HttpGet("documents")]
        public async Task<IActionResult> Documents(int id) =>
            this.ToActionResult(await slips.DocumentsAsync(id, HttpContext?.RequestAborted ?? default));

        // Doorgegeven aan de loods (afgedrukt of overhandigd); in deel 3 gebeurt dit bij verzenden.
        [HttpPost("documents/{documentId:int}/sent")]
        public async Task<IActionResult> Sent(int id, int documentId) =>
            this.ToActionResult(await slips.MarkSentAsync(id, documentId, HttpContext?.RequestAborted ?? default));
    }

    // Op de verkooporder-endpoints: na elke geslaagde POST of PUT op een order een nieuwe versie van de laadbon als die veranderd is (een
    // reservering, een toewijzing, emballage). Een fout hier laat de opdracht zelf nooit mislukken: het ophalen van de documenten vangt het op.
    public sealed class RefreshLoadingSlipAttribute : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            var method = context.HttpContext.Request.Method;
            if ((method != HttpMethods.Post && method != HttpMethods.Put) || executed.Exception != null) return;
            var status = executed.Result switch { ObjectResult o => o.StatusCode ?? 200, StatusCodeResult s => s.StatusCode, _ => 0 };
            if (status < 200 || status >= 300) return;
            if (!context.ActionArguments.TryGetValue("id", out var value) || value is not int orderId) return;
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<LoadingSlipService>().RefreshAsync(orderId, context.HttpContext.RequestAborted);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<RefreshLoadingSlipAttribute>>()
                    .LogWarning("Laadbon van verkooporder {OrderId} niet ververst ({Kind}); het ophalen van de documenten probeert het opnieuw.", orderId, e.GetType().Name);
            }
        }
    }
}
