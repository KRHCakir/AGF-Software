using AgfAdmin.Configuration;
using AgfAdmin.Data;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Services registreren
builder.Services.AddControllers()
    .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
// Aanmelding en rechten per gebied (voorlopig de standaardgebruiker uit Identity:DefaultUser; zie docs/rechten.md).
builder.Services.AddAgfSecurity(builder.Configuration);
builder.Services.AddSwaggerGen();

// Domeinservices (gedeeld met de tests die de echte API op een lege database starten).
builder.Services.AddAgfServices(builder.Environment.ContentRootPath);

// Database configuratie. In de demo-omgeving (AGF_DEMO_ENVIRONMENT=true) eerst de bewaking: alleen een database die met agf_demo
// begint, zodat de demo nooit op agf_smoke of een echte database draait (en daar dus ook niets migreert).
var connectionString = DatabaseConfiguration.GetRequiredConnectionString(builder.Configuration);
DemoEnvironment.EnsureAllowedDatabase(builder.Configuration, connectionString);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// CORS configuratie: standaard alleen de frontend op :3000. Een tweede lokale frontend (zoals de e2e-tests van de
// rondleiding op :3001) zet Cors__Origins__0=... in de omgeving van de API. In de demo-omgeving praat de browser via de website zelf
// (zelfde adres, /api): daar staat CORS geen enkele andere herkomst toe, ook niet als Cors:Origins iets zegt.
var corsOrigins = DemoEnvironment.IsEnabled(builder.Configuration)
    ? Array.Empty<string>()
    : builder.Configuration.GetSection("Cors:Origins").Get<string[]>() is { Length: > 0 } configured
        ? configured
        : new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy.WithOrigins(corsOrigins)
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

// QuestPDF License
QuestPDF.Settings.License = LicenseType.Community;

// Online achter Cloudflare Access: alleen verzoeken met een geldig Access-jeton (niet ingesteld = geen controle, zoals lokaal).
app.UseMiddleware<CloudflareAccessMiddleware>();
app.UseCors("AllowFrontend");
app.UseStaticFiles();
// 2. Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Gezondheidscontrole voor Docker Compose en een eenvoudige externe bewaking: de API draait en de database antwoordt.
app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "database unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

// 3. Database migratie en optionele demo-data (alleen Development + Development:SeedDemoData=true)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    // Onderweg / direct naar klant is er altijd (idempotent; zie StockLocationService).
    await StockLocationService.EnsureInTransitAsync(db);

    if (DevelopmentDataSeeder.IsEnabled(app.Environment, app.Configuration))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
        await seeder.SeedAsync(db);
    }
}

app.Run();