using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Infrastructure;

// Deterministische race: precies op het moment dat het eerste commando gaat opslaan, voert een andere context
// een concurrerend commando volledig uit. Beide zijn gebaseerd op dezelfde, eerder gelezen toestand.
public sealed class ConcurrentSaveInterceptor : SaveChangesInterceptor
{
    private readonly Func<Task> _concurrent;
    private bool _done;

    public ConcurrentSaveInterceptor(Func<Task> concurrent) => _concurrent = concurrent;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_done)
        {
            _done = true;
            await _concurrent();
        }
        return result;
    }
}
