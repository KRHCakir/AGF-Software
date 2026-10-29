using AgfAdmin.Configuration;
using AgfAdmin.Data;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Services registreren
builder.Services.AddControllers()
    .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AI & Email Services
builder.Services.AddScoped<IEmailIngestionService, EmailIngestionService>();
builder.Services.AddScoped<IAIExtractorService, MockAIExtractorService>();
builder.Services.AddScoped<ISalesParserService, SimpleSalesParserService>();
builder.Services.AddScoped<PurchaseInvoiceService>();
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<RelationService>();
builder.Services.AddScoped<PaymentConditionService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddSingleton<IEmailDomainChecker, DnsEmailDomainChecker>();
// Lokale referentielijst van goederencodes: bij het opstarten gelezen uit Data/Reference/goods-codes.json.
builder.Services.AddSingleton(_ => GoodsCodeCatalog.Load(Path.Combine(builder.Environment.ContentRootPath, GoodsCodeCatalog.DefaultRelativePath)));
builder.Services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();

// Beschikbaarheidsdomein
builder.Services.AddScoped<ISupplyCommitmentService, SupplyCommitmentService>();
builder.Services.AddScoped<IPalletService, PalletService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddScoped<IStockReservationService, StockReservationService>();
builder.Services.AddScoped<ISalesDispatchService, SalesDispatchService>();
builder.Services.AddScoped<ISalesDispatchCorrectionService, SalesDispatchCorrectionService>();
builder.Services.AddScoped<IDirectTradeAllocationService, DirectTradeAllocationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBusinessCalendar, AmsterdamBusinessCalendar>();
builder.Services.AddScoped<IDirectTradeShipmentService, DirectTradeShipmentService>();
builder.Services.AddScoped<IDirectTradeShipmentCorrectionService, DirectTradeShipmentCorrectionService>();
builder.Services.AddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();
builder.Services.AddScoped<IDeliveryRetractionService, DeliveryRetractionService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

// Database configuratie
var connectionString = DatabaseConfiguration.GetRequiredConnectionString(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// CORS configuratie: standaard alleen de frontend op :3000. Een tweede lokale frontend (zoals de e2e-tests van de
// rondleiding op :3001) zet Cors__Origins__0=... in de omgeving van de API.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() is { Length: > 0 } configured
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

app.UseCors("AllowFrontend");
app.UseStaticFiles();
// 2. Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

 // Gebruik de default policy
app.UseAuthorization();
app.MapControllers();

// 3. Database migratie en optionele demo-data (alleen Development + Development:SeedDemoData=true)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (DevelopmentDataSeeder.IsEnabled(app.Environment, app.Configuration))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
        await seeder.SeedAsync(db);
    }
}

app.Run();