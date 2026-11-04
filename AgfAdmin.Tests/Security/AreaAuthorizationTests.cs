using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using AgfAdmin.Controllers;
using AgfAdmin.Data;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgfAdmin.Tests.Security;

// Rechten per gebied: elk endpoint heeft het beleid van zijn gebied, en een testgebruiker zonder dat gebied krijgt 403. De
// standaardgebruiker (zonder ingestelde Permissions) heeft alles, zodat er nu voor niemand iets verandert.
public sealed class AreaAuthorizationTests
{
    private static async Task<(WebApplication App, HttpClient Client, SqliteAppDbContextFactory Db)> StartAsync(string displayName, params string[] permissions)
    {
        var db = new SqliteAppDbContextFactory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var settings = new Dictionary<string, string> { ["Identity:DefaultUser:DisplayName"] = displayName };
        for (var i = 0; i < permissions.Length; i++) settings[$"Identity:DefaultUser:Permissions:{i}"] = permissions[i];
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddControllers().AddApplicationPart(typeof(SalesOrdersController).Assembly);
        builder.Services.AddAgfSecurity(builder.Configuration);
        builder.Services.AddScoped<AppDbContext>(_ => db.CreateContext());
        builder.Services.AddSingleton<IBusinessCalendar>(db.Calendar);
        builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
        builder.Services.AddScoped<IStockReservationService, StockReservationService>();
        builder.Services.AddScoped<ISalesDispatchService, SalesDispatchService>();
        builder.Services.AddScoped<ISalesDispatchCorrectionService, SalesDispatchCorrectionService>();
        builder.Services.AddScoped<IDirectTradeAllocationService, DirectTradeAllocationService>();
        builder.Services.AddScoped<IDirectTradeShipmentService, DirectTradeShipmentService>();
        builder.Services.AddScoped<IDirectTradeShipmentCorrectionService, DirectTradeShipmentCorrectionService>();
        builder.Services.AddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();
        builder.Services.AddScoped<IDeliveryRetractionService, DeliveryRetractionService>();
        builder.Services.AddScoped<CountryService>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) }, db);
    }

    [Fact]
    public async Task A_user_without_Sales_gets_403_on_a_sales_endpoint_and_with_Sales_the_endpoint_answers()
    {
        var (app, client, db) = await StartAsync("Piet", Permissions.Purchase);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales-orders")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/sales-orders", new { })).StatusCode);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); db.Dispose(); }

        (app, client, db) = await StartAsync("Piet", Permissions.Sales);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sales-orders")).StatusCode);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); db.Dispose(); }
    }

    [Fact]
    public async Task Shared_master_data_can_be_read_by_everyone_and_changed_only_with_Stamgegevens()
    {
        var (app, client, db) = await StartAsync("Piet", Permissions.Sales);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/countries")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/countries/aliases", new { alias = "Holland", countryCode = "NL" })).StatusCode);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); db.Dispose(); }
    }

    [Fact]
    public async Task Without_configured_permissions_the_default_user_has_every_area_and_api_me_names_the_user()
    {
        var (app, client, db) = await StartAsync("Jan");
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sales-orders")).StatusCode);
            var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/me");
            Assert.Equal("Jan", me.DisplayName);
            Assert.Equal("admin", me.UserId);
            Assert.Equal(new[] { "Admin" }, me.Roles);
            Assert.Equal(Permissions.All.OrderBy(p => p), me.Permissions.OrderBy(p => p));
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); db.Dispose(); }
    }

    public static IEnumerable<object[]> Areas => new[]
    {
        new object[] { typeof(PurchaseOrdersController), Permissions.Purchase },
        new object[] { typeof(SupplyCommitmentsController), Permissions.Purchase },
        new object[] { typeof(PurchaseInvoicesController), Permissions.Purchase },
        new object[] { typeof(IncomingInvoicesController), Permissions.Purchase },
        new object[] { typeof(ReceiptDeviationsController), Permissions.Purchase },
        new object[] { typeof(AvailabilityController), Permissions.Stock },
        new object[] { typeof(InventoryController), Permissions.Stock },
        new object[] { typeof(PalletsController), Permissions.Stock },
        new object[] { typeof(SalesOrdersController), Permissions.Sales },
        new object[] { typeof(StockReservationsController), Permissions.Sales },
        new object[] { typeof(CustomersController), Permissions.Sales },
        new object[] { typeof(ArticlesController), Permissions.MasterDataWrites },
        new object[] { typeof(ProductsController), Permissions.MasterDataWrites },
        new object[] { typeof(RelationsController), Permissions.MasterDataWrites },
        new object[] { typeof(CountriesController), Permissions.MasterDataWrites },
        new object[] { typeof(SettingsController), Permissions.AdminWrites },
    };

    [Theory]
    [MemberData(nameof(Areas))]
    public void Every_area_controller_carries_the_policy_of_its_area(Type controller, string policy)
    {
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == policy);
    }

    [Fact]
    public void Every_controller_requires_a_signed_in_user_and_approving_needs_Keuring()
    {
        var controllers = typeof(SalesOrdersController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        Assert.All(controllers, c => Assert.NotEmpty(c.GetCustomAttributes<AuthorizeAttribute>()));
        var quality = typeof(PalletsController).GetMethods().Single(m => m.GetCustomAttributes<HttpPostAttribute>().Any(a => a.Template?.EndsWith("quality-status") == true));
        Assert.Contains(quality.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == Permissions.Inspection);
    }
}
