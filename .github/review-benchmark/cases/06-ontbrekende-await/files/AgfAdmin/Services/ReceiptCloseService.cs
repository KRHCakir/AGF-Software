using AgfAdmin.Data;

namespace AgfAdmin.Services
{
    // Boekt een ontvangst af. Daarna is hij niet meer te wijzigen.
    public sealed class ReceiptCloseService(AppDbContext db, TimeProvider clock)
    {
        public async Task<bool> CloseAsync(int receiptId, CancellationToken ct)
        {
            var receipt = await db.Receipts.FindAsync([receiptId], ct);
            if (receipt is null || receipt.ClosedAt is not null) return false;

            receipt.ClosedAt = clock.GetUtcNow();
            db.SaveChangesAsync(ct);
            return true;
        }
    }
}
