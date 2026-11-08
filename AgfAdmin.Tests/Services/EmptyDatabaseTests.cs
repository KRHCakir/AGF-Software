using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.Configuration;
using AgfAdmin.Controllers;
using AgfAdmin.Data;
using AgfAdmin.Security;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgfAdmin.Tests.Services;

// Schone start (scripts/new-clean-db.sh): de echte controllers en domeinservices (dezelfde registratie als Program.cs) op een verse,
// lege database. Elke lijst die een scherm opvraagt antwoordt 200 met niets erin, en een volledige keten werkt vanaf nul: relaties,
// artikel, inkooporder, ontvangst, keuring, verkoop, reserveren en verzenden.
public sealed class EmptyDatabaseTests : IAsyncLifetime
{
    private readonly SqliteAppDbContextFactory _db = new();
    private WebApplication _app;
    private HttpClient _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(SalesOrdersController).Assembly)
            .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
        builder.Services.AddAgfSecurity(builder.Configuration);
        builder.Services.AddAgfServices(AppContext.BaseDirectory);
        builder.Services.AddScoped<AppDbContext>(_ => _db.CreateContext());
        builder.Services.AddCors(options => options.AddPolicy("AllowFrontend", _ => { }));
        _app = builder.Build();
        _app.UseCors();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        _db.Dispose();
    }

    private async Task<JsonElement> Get(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> Post(string path, object body)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    // Wat de schermen op een lege database opvragen (Start, Relaties, Artikelen, Inkooporders, Ontvangst, Keuring, Beschikbaarheid,
    // Partijen, Verkooporders, Klantgebonden aanvoer, Inkoopfacturen en de keuzelijsten van de formulieren).
    public static IEnumerable<object[]> EmptyLists => new[]
    {
        "/api/relations?role=all&query=&includeInactive=true&page=1&pageSize=50", "/api/purchase-invoices/vendors", "/api/customers",
        "/api/articles", "/api/products?includeVariants=true", "/api/purchase-orders?status=all&page=1&pageSize=50",
        "/api/supply-commitments?route=Warehouse&status=Expected", "/api/supply-commitments?route=DirectTrade", "/api/partijen?status=all&page=1&pageSize=50",
        "/api/sales-orders", "/api/sales-orders/awaiting-stock", "/api/purchase-invoices", "/api/articles/origin-cleanup",
    }.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(EmptyLists))]
    public async Task Every_list_a_screen_asks_for_answers_200_and_is_empty(string path)
    {
        var body = await Get(path);
        var items = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items");
        Assert.Equal(0, items.GetArrayLength());
        if (body.ValueKind == JsonValueKind.Object) Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Availability_reference_lists_and_settings_answer_on_an_empty_database()
    {
        Assert.Equal(0, (await Get("/api/availability?route=Warehouse")).GetProperty("variants").GetArrayLength());
        Assert.True((await Get("/api/countries")).GetProperty("countries").GetArrayLength() > 200);
        Assert.Equal("Auto", (await Get("/api/partijen/next-number")).GetProperty("mode").GetString());
        Assert.False(string.IsNullOrEmpty((await Get("/api/partijen/next-number")).GetProperty("suggestion").GetString()));
        await Get("/api/articles/reference");
        await Get("/api/relations/reference");
        await Get("/api/purchase-invoices/settings");
        await Get("/api/me");
    }

    [Fact]
    public async Task A_full_chain_works_from_nothing_relations_article_order_receipt_inspection_sales_and_dispatch()
    {
        await Post("/api/relations", new { requestId = Guid.NewGuid(), searchCode = "KWEKER", name = "Eerste Kwekerij", preferredLanguage = "nl", vendor = new { } });
        await Post("/api/relations", new { requestId = Guid.NewGuid(), searchCode = "KLANT", name = "Eerste Klant", preferredLanguage = "nl", customer = new { } });
        var vendorId = (await Get("/api/purchase-invoices/vendors"))[0].GetProperty("id").GetInt32();
        var customerId = (await Get("/api/customers"))[0].GetProperty("id").GetInt32();

        var article = await Post("/api/articles", new
        {
            productName = "Avocado", variantName = "Hass", shortcutCode = "AVOPE18", originCountryCode = "PE", sizeCategory = "18", tradeClass = "I",
            packaging = "4 kg doos", packagingUnit = "colli", weightMethod = "Calculated", defaultNetKgPerPackage = 4, defaultPackagesPerPallet = 240,
        });
        var variantId = article.GetProperty("id").GetInt32();

        var order = await Post("/api/supply-commitments", new
        {
            requestId = Guid.NewGuid(), vendorId, route = "Warehouse", expectedAt = DateTimeOffset.UtcNow.AddDays(1), supplierReference = "EERSTE",
            newPartij = new { number = (string)null },
            lines = new[] { new { productVariantId = variantId, expectedQuantity = 10, expectedUnit = "colli" } },
        });
        Assert.Matches(@"^IO-\d{4}-\d+$", order.GetProperty("orderNumber").GetString());
        var orderLineId = order.GetProperty("lines")[0].GetProperty("id").GetInt32();

        var pallet = await Post("/api/pallets", new { requestId = Guid.NewGuid(), logisticsStatus = "Received", locationContext = "Warehouse", warehouseLocation = "A-01" });
        var content = await Post($"/api/pallets/{pallet.GetProperty("id").GetInt32()}/content-lines", new
        {
            requestId = Guid.NewGuid(), supplyCommitmentLineId = orderLineId, quantity = 10, unit = "colli", weightMethod = "Calculated", netKgPerPackage = 4,
        });
        var contentId = content.GetProperty("id").GetInt32();
        await Post($"/api/pallets/{pallet.GetProperty("id").GetInt32()}/content-lines/{contentId}/quality-status", new
        {
            requestId = Guid.NewGuid(), expectedContentLineVersion = content.GetProperty("version").GetInt32(), toStatus = "Approved", inspectedBy = "Keurder",
        });

        var sale = await Post("/api/sales-orders", new
        {
            requestId = Guid.NewGuid(), customerId, customerReference = "EERSTE-VERKOOP", requestedDeliveryDate = DateTimeOffset.UtcNow.AddDays(2),
            lines = new[] { new { productVariantId = variantId, quantity = 4, unit = "colli" } },
        });
        var saleId = sale.GetProperty("id").GetInt32();
        var saleLineId = sale.GetProperty("lines")[0].GetProperty("id").GetInt32();
        await Post($"/api/sales-orders/{saleId}/lines/{saleLineId}/reservations", new { requestId = Guid.NewGuid(), palletContentLineId = contentId, quantity = 4, unit = "colli", netWeightKg = 16 });

        var reserved = await Get($"/api/sales-orders/{saleId}");
        var reservation = reserved.GetProperty("lines")[0].GetProperty("reservations")[0];
        await Post($"/api/sales-orders/{saleId}/dispatches", new
        {
            requestId = Guid.NewGuid(), expectedOrderVersion = reserved.GetProperty("version").GetInt32(),
            lines = new[] { new { stockReservationId = reservation.GetProperty("id").GetInt32(), expectedReservationVersion = reservation.GetProperty("version").GetInt32() } },
        });

        var shipped = (await Get($"/api/sales-orders/{saleId}")).GetProperty("lines")[0];
        Assert.Equal(4m, shipped.GetProperty("dispatchedQuantity").GetDecimal());
        // De keten staat in de partij: één inkoop-partij met deze order, ontvangen 10 en verkocht 4, en één verkoop-partij met de verkooporder.
        var partijen = await Get("/api/partijen?status=all&page=1&pageSize=50");
        Assert.Equal(1, partijen.GetProperty("total").GetInt32());
        await using var check = _db.CreateContext();
        Assert.Equal(1, await check.Partijen.CountAsync(p => p.Kind == "Purchase"));
        Assert.Equal(saleId, (await check.Partijen.SingleAsync(p => p.Kind == "Sales")).SalesOrderId);
        Assert.Equal(1, await check.SalesDispatches.CountAsync());
    }
}
