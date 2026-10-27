using AgfAdmin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/relations/{relationId:int}/bank-accounts")]
    public sealed class BankAccountsController(AppDbContext db) : ControllerBase
    {
        [HttpDelete("{id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> Delete(int relationId, int id, CancellationToken ct)
        {
            var account = await db.BankAccounts.FindAsync([id], ct);
            if (account is null || account.CustomerId != relationId) return NotFound();
            db.BankAccounts.Remove(account);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
