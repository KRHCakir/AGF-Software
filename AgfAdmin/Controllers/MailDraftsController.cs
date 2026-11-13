using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Mailconcepten bij de documenten van een verkooporder (documenten deel 3), onder Verkoop. Hier wordt niets vanzelf verstuurd.
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    public class MailDraftsController(MailDraftService drafts) : ControllerBase
    {
        // De concepten van één order (maakt een ontbrekend concept voor een nieuw document alsnog aan).
        [HttpGet("api/sales-orders/{id:int}/mail-drafts")]
        public async Task<IActionResult> ForOrder(int id) => Ok(await drafts.ForOrderAsync(id, HttpContext?.RequestAborted ?? default));

        // Te versturen: open en mislukte concepten over alle orders.
        [HttpGet("api/mail-drafts")]
        public async Task<IActionResult> Open() => Ok(await drafts.OpenAsync(HttpContext?.RequestAborted ?? default));

        [HttpGet("api/mail-drafts/{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await drafts.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpPut("api/mail-drafts/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateMailDraftDto request) =>
            this.ToActionResult(await drafts.UpdateAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Verwijderen: alleen het concept, het document blijft.
        [HttpPost("api/mail-drafts/{id:int}/discard")]
        public async Task<IActionResult> Discard(int id, [FromBody] MailDraftActionDto request) =>
            this.ToActionResult(await drafts.DiscardAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
