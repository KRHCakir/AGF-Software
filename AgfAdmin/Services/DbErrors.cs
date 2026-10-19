using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Herkenning van databasefouten die een commando mag vertalen naar 409. Alles wat hier niet onder valt
    // (bijvoorbeeld een check-constraint, een FK of een verbindingsfout) blijft een echte fout en wordt niet verhuld.
    public static class DbErrors
    {
        // Optimistische concurrency: een andere schrijver was eerder (versie-token in de WHERE-clausule).
        public static bool IsConcurrencyConflict(DbUpdateException exception) => exception is DbUpdateConcurrencyException;

        // Botsende unieke sleutel (idempotentiesleutel): PostgreSQL 23505 of SQLite "UNIQUE constraint failed".
        public static bool IsUniqueViolation(DbUpdateException exception)
        {
            for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
            {
                if (inner is Npgsql.PostgresException postgres)
                {
                    return postgres.SqlState == "23505";
                }
                if (inner.GetType().Name == "SqliteException")
                {
                    return inner.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal);
                }
            }
            return false;
        }
    }
}
