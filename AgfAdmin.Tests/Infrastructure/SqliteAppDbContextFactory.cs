using AgfAdmin.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Infrastructure;

// Echte relationele testdatabase (SQLite in-memory) zodat FK's, unieke indexen en
// delete-gedrag door de database zelf worden afgedwongen, anders dan bij EF InMemory.
// Alleen voor tests: het schema wordt hier met EnsureCreated opgebouwd, nooit in de applicatie.
public sealed class SqliteAppDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteAppDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateContext() => new(_options);

    // Voor deterministische concurrency-tests: een interceptor kan midden in SaveChanges een gelijktijdige wijziging doen.
    public AppDbContext CreateContext(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options);

    public void Dispose() => _connection.Dispose();
}
