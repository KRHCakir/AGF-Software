using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AgfAdmin.Tests.Services;

public class DevelopmentDataSeederTests
{
    private static AppDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options);
    }

    private static string NewDatabaseName() => Guid.NewGuid().ToString();

    [Fact]
    public async Task SeedAsync_Seeds_An_Empty_Development_Database()
    {
        var databaseName = NewDatabaseName();
        await using (var db = CreateContext(databaseName))
        {
            await new DevelopmentDataSeeder().SeedAsync(db);
        }

        await using var verify = CreateContext(databaseName);
        Assert.Equal(2, await verify.Products.CountAsync());
        Assert.Equal(3, await verify.ProductVariants.CountAsync());
        Assert.Equal(1, await verify.Vendors.CountAsync());
        Assert.Equal(2, await verify.IncomingInvoices.CountAsync());
        Assert.Equal(1, await verify.IncomingInvoiceLines.CountAsync());
        Assert.Equal(1, await verify.Customers.CountAsync());
        Assert.Equal(3, await verify.Shipments.CountAsync());
        Assert.True(await verify.ProductVariants.AnyAsync(v => v.ShortcutCode == "avohassper22"));
    }

    [Fact]
    public async Task SeedAsync_Is_Idempotent()
    {
        var databaseName = NewDatabaseName();
        await using (var db = CreateContext(databaseName))
        {
            await new DevelopmentDataSeeder().SeedAsync(db);
        }
        await using (var db = CreateContext(databaseName))
        {
            await new DevelopmentDataSeeder().SeedAsync(db);
        }

        await using var verify = CreateContext(databaseName);
        Assert.Equal(2, await verify.Products.CountAsync());
        Assert.Equal(3, await verify.ProductVariants.CountAsync());
        Assert.Equal(1, await verify.Vendors.CountAsync());
        Assert.Equal(2, await verify.IncomingInvoices.CountAsync());
        Assert.Equal(1, await verify.IncomingInvoiceLines.CountAsync());
        Assert.Equal(1, await verify.Customers.CountAsync());
        Assert.Equal(3, await verify.Shipments.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_Does_Not_Delete_Existing_Data()
    {
        var databaseName = NewDatabaseName();
        await using (var db = CreateContext(databaseName))
        {
            db.Customers.Add(new Customer { Name = "Sentinel Customer", Email = "sentinel@example.test" });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName))
        {
            await new DevelopmentDataSeeder().SeedAsync(db);
        }

        await using var verify = CreateContext(databaseName);
        var customer = Assert.Single(await verify.Customers.ToListAsync());
        Assert.Equal("Sentinel Customer", customer.Name);
        Assert.Equal("sentinel@example.test", customer.Email);
    }

    [Fact]
    public async Task SeedAsync_Does_Not_Overwrite_Existing_Products()
    {
        var databaseName = NewDatabaseName();
        await using (var db = CreateContext(databaseName))
        {
            db.Products.Add(new Product { Name = "Sentinel Product", ImageUrl = "sentinel.png" });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName))
        {
            await new DevelopmentDataSeeder().SeedAsync(db);
        }

        await using var verify = CreateContext(databaseName);
        var product = Assert.Single(await verify.Products.ToListAsync());
        Assert.Equal("Sentinel Product", product.Name);
        Assert.Equal("sentinel.png", product.ImageUrl);
        Assert.Equal(0, await verify.ProductVariants.CountAsync());
    }

    [Fact]
    public void IsEnabled_Development_With_Explicit_True_Returns_True()
    {
        var configuration = BuildConfiguration("true");

        Assert.True(DevelopmentDataSeeder.IsEnabled(new FakeHostEnvironment(Environments.Development), configuration));
    }

    [Fact]
    public void IsEnabled_Development_Without_Setting_Returns_False()
    {
        var configuration = BuildConfiguration(null);

        Assert.False(DevelopmentDataSeeder.IsEnabled(new FakeHostEnvironment(Environments.Development), configuration));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    public void IsEnabled_Development_With_False_Or_Invalid_Setting_Returns_False(string value)
    {
        var configuration = BuildConfiguration(value);

        Assert.False(DevelopmentDataSeeder.IsEnabled(new FakeHostEnvironment(Environments.Development), configuration));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void IsEnabled_Non_Development_With_True_Returns_False(string environmentName)
    {
        var configuration = BuildConfiguration("true");

        Assert.False(DevelopmentDataSeeder.IsEnabled(new FakeHostEnvironment(environmentName), configuration));
    }

    private static IConfiguration BuildConfiguration(string seedDemoData)
    {
        var values = new Dictionary<string, string>();
        if (seedDemoData != null)
        {
            values["Development:SeedDemoData"] = seedDemoData;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "AgfAdmin.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
