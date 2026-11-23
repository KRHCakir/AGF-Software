using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Services;

// Sorteren op kolomkoppen (feedback 22 november, prompt 2): lijsten die op de server bladeren, sorteren op de server over alle rijen; alleen
// velden uit de toegestane lijst; tekst zonder hoofdletters en accenten, getallen en datums als getal en datum, nummers natuurlijk.
public class ListSortTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 22)) };

    public void Dispose() => _factory.Dispose();

    private static CurrentUser User() => new("id-jan", "Jan", new[] { "Medewerker" }, Permissions.All);

    private sealed record Row(int Id, string Text, decimal? Amount, DateOnly? Date);

    private static readonly IReadOnlyDictionary<string, SortField<Row>> RowFields = new Dictionary<string, SortField<Row>>
    {
        ["text"] = new(SortKind.Text, r => r.Text),
        ["number"] = new(SortKind.Natural, r => r.Text),
        ["amount"] = new(SortKind.Number, r => r.Amount),
        ["date"] = new(SortKind.Date, r => r.Date),
    };

    private static int[] Sorted(IEnumerable<Row> rows, string field, bool descending) =>
        ListSort.Apply(rows, new SortRequest(field, descending), RowFields).Select(r => r.Id).ToArray();

    [Theory]
    [InlineData("2", "10")]
    [InlineData("I-2", "I-10")]
    [InlineData("V-9", "V-10")]
    [InlineData("P-10", "p-11")]
    [InlineData("261109", "2611010")]
    [InlineData("IO-2026-0002", "IO-2026-0010")]
    [InlineData("I-10", "V-1")]
    [InlineData("007", "8")]
    [InlineData("partij 2b", "partij 10a")]
    public void Natural_Order_Puts_The_First_Before_The_Second(string first, string second)
    {
        Assert.True(ListSort.CompareNatural(first, second) < 0, $"{first} < {second}");
        Assert.True(ListSort.CompareNatural(second, first) > 0, $"{second} > {first}");
    }

    [Fact]
    public void Lot_Numbers_Sort_Naturally()
    {
        var numbers = new[] { "2611010", "P-10", "261109", "P-9", "26110100", "p-11" };
        var rows = numbers.Select((n, i) => new Row(i, n, null, null)).ToList();
        Assert.Equal(new[] { "261109", "2611010", "26110100", "P-9", "P-10", "p-11" }, Sorted(rows, "number", false).Select(i => numbers[i]));
    }

    [Fact]
    public void Text_Ignores_Capitals_And_Accents_And_Empty_Stays_Last_In_Both_Directions()
    {
        var rows = new[] { new Row(1, "Zuid-Afrika", 100m, new DateOnly(2026, 11, 3)), new Row(2, "éénhoorn", 9.5m, new DateOnly(2026, 1, 15)), new Row(3, "appel", null, null), new Row(4, "Banaan", 1000m, new DateOnly(2025, 12, 31)), new Row(5, " ", -5m, new DateOnly(2026, 11, 22)) };
        Assert.Equal(new[] { 3, 4, 2, 1, 5 }, Sorted(rows, "text", false));
        Assert.Equal(new[] { 1, 2, 4, 3, 5 }, Sorted(rows, "text", true));
        Assert.Equal(new[] { 5, 2, 1, 4, 3 }, Sorted(rows, "amount", false));
        Assert.Equal(new[] { 4, 1, 2, 5, 3 }, Sorted(rows, "amount", true));
        Assert.Equal(new[] { 4, 2, 1, 5, 3 }, Sorted(rows, "date", false));
        Assert.Equal(new[] { 5, 1, 2, 4, 3 }, Sorted(rows, "date", true));
    }

    [Fact]
    public void Equal_Values_Keep_The_Default_Order_And_No_Request_Keeps_Everything()
    {
        var rows = new[] { new Row(3, "Gelijk", 1m, null), new Row(1, "gelijk", 1m, null), new Row(2, "GELIJK", 1m, null) };
        Assert.Equal(new[] { 3, 1, 2 }, Sorted(rows, "text", true));
        Assert.Equal(new[] { 3, 1, 2 }, ListSort.Apply(rows, null, RowFields).Select(r => r.Id));
    }

    [Theory]
    [InlineData("name; drop table", null, "sort")]
    [InlineData("Vendor.Name", null, "sort")]
    [InlineData("vendor", "sideways", "direction")]
    [InlineData(null, "up", "direction")]
    public void Unknown_Field_Or_Direction_Is_Refused(string sort, string direction, string errorKey)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(ListSort.Parse(sort, direction, ListSortFields.PurchaseOrders.Keys, errors));
        Assert.Contains(errorKey, errors.Keys);
    }

    [Fact]
    public void Known_Field_Is_Taken_Case_Insensitively_Back_To_The_Allowed_Name()
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Equal(new SortRequest("vendor", true), ListSort.Parse(" Vendor ", "DESC", ListSortFields.PurchaseOrders.Keys, errors));
        Assert.Equal(new SortRequest("netKg", false), ListSort.Parse("netkg", null, ListSortFields.PurchaseOrders.Keys, errors));
        Assert.Null(ListSort.Parse("", "", ListSortFields.PurchaseOrders.Keys, errors));
        Assert.Empty(errors);
    }

    // ---------------- Over meerdere pagina's ----------------

    [Fact]
    public async Task Purchase_Orders_Sort_Over_All_Pages()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var references = new[] { "FO-10", "FO-2", null, "fo-9", "FO-1" };
        var ids = new List<int>();
        foreach (var (reference, index) in references.Select((r, i) => (r, i)))
        {
            var request = PurchaseOrderTestData.OrderRequest(seed, vendorId: index % 2 == 0 ? seed.VendorId : seed.OtherVendorId);
            request.SupplierReference = reference;
            ids.Add((await PurchaseOrderTestData.CreateOrderAsync(_factory, request)).Id);
        }
        await using var db = _factory.CreateContext();
        var service = new PurchaseOrderService(db);
        async Task<string[]> Numbers(string field, bool descending)
        {
            var pages = new List<PurchaseOrderListItemDto>();
            for (var page = 1; page <= 3; page++) pages.AddRange((await service.ListAsync(null, "all", null, page, 2, sort: new SortRequest(field, descending))).Value.Items);
            return pages.Select(i => i.SupplierReference ?? i.OrderNumber).ToArray();
        }

        // Standaard: nieuwste eerst. Op nummer: natuurlijk, met het eigen IO-nummer waar de leverancier geen nummer gaf.
        var defaultOrder = (await service.ListAsync(null, "all", null, 1, 10)).Value.Items.Select(i => i.Id).ToArray();
        Assert.Equal(Enumerable.Reverse(ids).ToArray(), defaultOrder);
        var own = (await service.ListAsync(null, "all", null, 1, 10)).Value.Items.Single(i => i.SupplierReference == null).OrderNumber;
        Assert.Equal(new[] { "FO-1", "FO-2", "fo-9", "FO-10", own }, await Numbers("number", false));
        Assert.Equal(new[] { own, "FO-10", "fo-9", "FO-2", "FO-1" }, await Numbers("number", true));

        // Op leverancier: Fruitimport Oost voor Nature's Pride, binnen een leverancier de standaardvolgorde (nieuwste eerst).
        var byVendor = new List<PurchaseOrderListItemDto>();
        for (var page = 1; page <= 3; page++) byVendor.AddRange((await service.ListAsync(null, "all", null, page, 2, sort: new SortRequest("vendor", false))).Value.Items);
        Assert.Equal(new[] { "Fruitimport Oost", "Fruitimport Oost", "Nature's Pride", "Nature's Pride", "Nature's Pride" }, byVendor.Select(i => i.VendorName));
        Assert.Equal(new[] { ids[3], ids[1], ids[4], ids[2], ids[0] }, byVendor.Select(i => i.Id));
        Assert.Equal(5, (await service.ListAsync(null, "all", null, 1, 2, sort: new SortRequest("vendor", false))).Value.Total);
    }

    [Fact]
    public async Task Partijen_Sort_Naturally_Over_All_Pages()
    {
        var numbers = new[] { "P-10", "P-2", "P-1", "P-9", "P-100" };
        foreach (var number in numbers)
        {
            await using var create = _factory.CreateContext();
            Assert.Equal(ServiceResultStatus.Created, (await new PartijService(create).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number })).Status);
        }
        await using var db = _factory.CreateContext();
        var service = new PartijService(db);
        async Task<string[]> All(SortRequest sort, string status = null)
        {
            var items = new List<string>();
            for (var page = 1; page <= 3; page++) items.AddRange((await service.ListAsync(null, status, page, 2, sort: sort)).Value.Items.Select(i => i.Number));
            return items.ToArray();
        }

        Assert.Equal(new[] { "P-100", "P-9", "P-1", "P-2", "P-10" }, await All(null));
        Assert.Equal(new[] { "P-1", "P-2", "P-9", "P-10", "P-100" }, await All(new SortRequest("number", false)));
        Assert.Equal(new[] { "P-100", "P-10", "P-9", "P-2", "P-1" }, await All(new SortRequest("number", true)));
        Assert.Equal(new[] { "P-1", "P-2", "P-9", "P-10", "P-100" }, await All(new SortRequest("number", false), "Open"));
        Assert.Equal(5, (await service.ListAsync(null, null, 1, 2, sort: new SortRequest("number", false))).Value.Total);
    }

    [Fact]
    public async Task Relations_Sort_Over_All_Pages()
    {
        foreach (var name in new[] { "Zeeuwse Fruit", "appelhof", "Ébène Import", "Bananen BV" })
        {
            await using var create = _factory.CreateContext();
            Assert.Equal(ServiceResultStatus.Created, (await new RelationService(create).CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = name, Vendor = new SaveVendorRoleDto() })).Status);
        }
        await using var db = _factory.CreateContext();
        var service = new RelationService(db);
        async Task<string[]> All(SortRequest sort)
        {
            var items = new List<string>();
            for (var page = 1; page <= 2; page++) items.AddRange((await service.ListAsync("all", null, false, page, 3, sort: sort)).Items.Select(i => i.Name));
            return items.ToArray();
        }
        Assert.Equal(new[] { "Zeeuwse Fruit", "Ébène Import", "Bananen BV", "appelhof" }, await All(new SortRequest("name", true)));
        Assert.Equal(new[] { "appelhof", "Bananen BV", "Ébène Import", "Zeeuwse Fruit" }, await All(new SortRequest("name", false)));
    }

    [Fact]
    public async Task Certificates_Sort_Naturally_Over_All_Pages()
    {
        await using (var create = _factory.CreateContext())
        {
            var relation = (await new RelationService(create).CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Kwekerij Zon", Vendor = new SaveVendorRoleDto() })).Value;
            var certificates = new[] { "4063061000010", "SKAL-2", "SKAL-10", "SKAL-1" }
                .Select(number => new SaveCertificateDto { TypeCode = "Skal", Number = number, ValidUntil = new DateOnly(2027, 1, 1), IsActive = true }).ToList();
            var saved = await new CertificateService(create, User()).SaveForRelationAsync(relation.Id, new SaveCertificatesDto { ExpectedVersion = "0", Certificates = certificates });
            Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        }
        await using var db = _factory.CreateContext();
        var service = new CertificateService(db, User());
        var numbers = new List<string>();
        for (var page = 1; page <= 2; page++) numbers.AddRange((await service.OverviewAsync(null, null, null, false, page, 2, default, new SortRequest("number", false))).Items.Select(i => i.Number));
        Assert.Equal(new[] { "4063061000010", "SKAL-1", "SKAL-2", "SKAL-10" }, numbers);
    }

    // ---------------- De API weigert een vrij veld ----------------

    [Fact]
    public async Task Api_Refuses_An_Unknown_Sort_Field_With_400_On_Every_Server_List()
    {
        await using var db = _factory.CreateContext();
        AssertRefused(await new PurchaseOrdersController(new PurchaseOrderService(db)).List(null, "all", null, sort: "Id"));
        AssertRefused(await new PartijenController(new PartijService(db)).List(null, null, null, sort: "NumberKey"));
        AssertRefused(await new RelationsController(new RelationService(db)).List("all", null, sort: "IbanNumber"));
        AssertRefused(await new CertificatesController(new CertificateService(db, User())).Overview(null, null, null, sort: "number", direction: "random"));
        Assert.IsType<OkObjectResult>(await new PurchaseOrdersController(new PurchaseOrderService(db)).List(null, "all", null, sort: "vendor", direction: "desc"));
        Assert.IsType<OkObjectResult>(await new PartijenController(new PartijService(db)).List(null, null, null, sort: "number"));
    }

    private static void AssertRefused(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.True(problem.Errors.ContainsKey("sort") || problem.Errors.ContainsKey("direction"));
    }
}
