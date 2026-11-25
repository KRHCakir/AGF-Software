using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1: de lijst Verkoopfacturen (zoeken op nummer, klant, V- en I-nummer ook zonder letter, filter op status, sorteren op de
// server, bladeren) en de omzetsoorten onder Beheer.
public sealed class SalesInvoiceListTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<SalesInvoiceListDto>> ListAsync(string query = null, string status = null, string sort = null, string direction = null, int page = 1, int pageSize = 50)
    {
        await using var db = _factory.CreateContext();
        return await new SalesInvoiceService(db).ListAsync(query, status, sort, direction, page, pageSize);
    }

    // Drie facturen: een directe met partij I-2611001 (definitief), een directe met alleen een dienstregel (concept) en een oude factuur.
    private async Task<(SalesInvoiceDto Final, SalesInvoiceDto Concept)> ArrangeAsync()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var final = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 4m))).Value;
        await using (var db = _factory.CreateContext())
        {
            final = (await new SalesInvoiceService(db).FinalizeAsync(final.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = final.Version })).Value;
            db.Invoices.Add(new Invoice { InvoiceNumber = "FAC-20261020-1234", Status = "Nieuw", CustomCustomerName = "Oud", InvoiceDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var concept = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.OtherCustomerId, DirectInvoiceTestData.Service("Stroom koelcel", 80m))).Value;
        return (final, concept);
    }

    [Fact]
    public async Task The_List_Shows_Only_Sales_Invoices_Concepts_First_And_Filters_By_Status()
    {
        var (final, concept) = await ArrangeAsync();
        var all = (await ListAsync()).Value;
        Assert.Equal(2, all.Total);
        Assert.Equal(new[] { concept.Id, final.Id }, all.Items.Select(i => i.Id));
        Assert.Equal(new[] { "I-2611001" }, all.Items[1].PurchaseLotNumbers);
        Assert.Equal(final.SalesLotNumber, all.Items[1].SalesLotNumbers.Single());
        Assert.Equal((4m * 12m, 4.32m), (all.Items[1].Net, all.Items[1].Vat));

        Assert.Equal(new[] { concept.Id }, (await ListAsync(status: "open")).Value.Items.Select(i => i.Id));
        Assert.Equal(new[] { final.Id }, (await ListAsync(status: SalesInvoiceRules.StatusFinal)).Value.Items.Select(i => i.Id));
        Assert.Equal(ServiceResultStatus.Validation, (await ListAsync(status: "Paid")).Status);
    }

    [Theory]
    [InlineData("VF-", true)]
    [InlineData("customer t", true)]
    [InlineData("2611001", true)]
    [InlineData("i 2611001", true)]
    [InlineData("onbekend", false)]
    public async Task Search_Finds_Number_Customer_And_Lots_Also_Without_Letter_Or_Dash(string query, bool found)
    {
        var (final, _) = await ArrangeAsync();
        var items = (await ListAsync(query)).Value.Items;
        Assert.Equal(found, items.Any(i => i.Id == final.Id));
    }

    [Fact]
    public async Task Search_Finds_The_V_Number_And_Sorting_Is_On_The_Server_With_Paging()
    {
        var (final, concept) = await ArrangeAsync();
        Assert.Equal(final.Id, (await ListAsync(final.SalesLotNumber.Replace("-", ""))).Value.Items.Single().Id);

        var byCustomer = (await ListAsync(sort: "customer", direction: "desc")).Value.Items;
        Assert.Equal(new[] { "Customer U", "Customer T" }, byCustomer.Select(i => i.CustomerName));
        var paged = (await ListAsync(sort: "customer", direction: "asc", page: 2, pageSize: 1)).Value;
        Assert.Equal((2, "Customer U"), (paged.Total, paged.Items.Single().CustomerName));
        Assert.Equal(ServiceResultStatus.Validation, (await ListAsync(sort: "Kind")).Status);
    }

    [Fact]
    public async Task Revenue_Types_Have_One_Active_Default_And_Are_Never_Deleted()
    {
        await using var db = _factory.CreateContext();
        var service = new RevenueTypeService(db);
        var standard = Assert.Single(await service.ListAsync(false));
        Assert.Equal(("Omzet diensten", true), (standard.Name, standard.IsDefault));

        var transport = await service.CreateAsync(new SaveRevenueTypeDto { Name = "Omzet transport", ExternalCode = "8010" });
        Assert.Equal(ServiceResultStatus.Created, transport.Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.CreateAsync(new SaveRevenueTypeDto { Name = "omzet TRANSPORT" })).Status);

        var refused = await service.UpdateAsync(standard.Id, new SaveRevenueTypeDto { Name = standard.Name, IsActive = false, ExpectedVersion = standard.Version });
        Assert.Contains("isActive", refused.Errors.Keys);

        var newDefault = await service.UpdateAsync(transport.Value.Id, new SaveRevenueTypeDto { Name = "Omzet transport", ExternalCode = "8010", IsDefault = true, ExpectedVersion = transport.Value.Version });
        Assert.Equal(ServiceResultStatus.Ok, newDefault.Status);
        var list = await service.ListAsync(true);
        Assert.Equal(new[] { ("Omzet transport", true), ("Omzet diensten", false) }, list.Select(r => (r.Name, r.IsDefault)));

        var old = list.Single(r => r.Name == "Omzet diensten");
        Assert.Equal(ServiceResultStatus.Ok, (await service.UpdateAsync(old.Id, new SaveRevenueTypeDto { Name = old.Name, IsActive = false, ExpectedVersion = old.Version })).Status);
        Assert.Equal(new[] { "Omzet transport" }, (await service.ListAsync(false)).Select(r => r.Name));
        Assert.Equal(ServiceResultStatus.Conflict, (await service.UpdateAsync(old.Id, new SaveRevenueTypeDto { Name = old.Name, ExpectedVersion = old.Version })).Status);
    }
}
