using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 1, de API: certificaten bij een relatie (meerdere producten, meerdere van dezelfde soort, GGN-controle), gecontroleerd door
// CurrentUser, bestanden als versies, het overzicht van bijna verlopen certificaten (klant en leverancier), de relatielijst en het
// GGN-voorstel bij ontvangst.
public partial class CertificateServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(Today) };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agf-cert-tests-" + Guid.NewGuid().ToString("N"));
    private string _user = "Anja";

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private CertificateService Service(AgfAdmin.Data.AppDbContext db) =>
        new(db, new CurrentUser($"id-{_user}", _user, new[] { "Medewerker" }, Permissions.All), new FileDocumentStore(_root));

    private sealed record Seed(int VendorRelation, int CustomerRelation, int VendorId, int AvocadoId, int MangoId, int LimeId);

    private async Task<Seed> SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var avocado = new Product { Name = "Avocado" };
        var mango = new Product { Name = "Mango" };
        var lime = new Product { Name = "Limoen" };
        var vendor = new Vendor { Name = "Teler Uno" };
        var customer = new Customer { Name = "Klant Groot" };
        db.AddRange(avocado, mango, lime, vendor, customer);
        await db.SaveChangesAsync();
        return new Seed(vendor.RelationId, customer.RelationId, vendor.Id, avocado.Id, mango.Id, lime.Id);
    }

    private static SaveCertificateDto Ggn(string number, DateOnly? until, params int[] products) =>
        new() { TypeCode = CertificateRules.GlobalGapIfa, Number = number, ValidUntil = until, ProductIds = products.ToList() };

    private async Task<ServiceResult<CertificateListDto>> SaveAsync(int relationId, string version, params SaveCertificateDto[] items)
    {
        await using var db = _factory.CreateContext();
        return await Service(db).SaveForRelationAsync(relationId, new SaveCertificatesDto { ExpectedVersion = version, Certificates = items.ToList() });
    }

    private async Task<CertificateListDto> GetAsync(int relationId)
    {
        await using var db = _factory.CreateContext();
        return (await Service(db).GetForRelationAsync(relationId)).Value;
    }

    [Fact]
    public async Task A_Supplier_Can_Have_Two_Ggns_For_Different_Products_And_Other_Certificates()
    {
        var seed = await SeedAsync();
        var saved = await SaveAsync(seed.VendorRelation, "0",
            Ggn("4049 9284 15684", Today.AddDays(200), seed.MangoId, seed.LimeId),
            Ggn("4049929000002", Today.AddDays(10), seed.AvocadoId),
            new SaveCertificateDto { TypeCode = CertificateRules.GlobalGapCoc, Number = "CoC-NL-2026/000123456789-ABC", CertificationBody = "Control Union", ValidFrom = Today.AddDays(-100), ValidUntil = Today.AddDays(-1) },
            new SaveCertificateDto { TypeCode = CertificateRules.Custom, CustomTypeName = "Demeter", Number = "D-1" });
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        var list = saved.Value.Certificates;
        Assert.Equal(new[] { "4049928415684", "4049929000002", "CoC-NL-2026/000123456789-ABC", "D-1" }, list.Select(c => c.Number));
        Assert.Equal(new[] { "Limoen", "Mango" }, list[0].Products.Select(p => p.Name));
        Assert.Equal(new[] { CertificateRules.StatusValid, CertificateRules.StatusExpiringSoon, CertificateRules.StatusExpired, CertificateRules.StatusNoEndDate }, list.Select(c => c.Status));
        Assert.Equal(new int?[] { 200, 10, -1, null }, list.Select(c => c.DaysLeft));
        Assert.Equal("Demeter", list[3].TypeName);
        // De eigen soort staat daarna in de keuzelijst.
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(new[] { "Demeter" }, (await Service(db).ReferenceAsync()).CustomTypes);
        }
        // De check digit van 4049929000002 klopt niet: een waarschuwing, geen blokkade.
        Assert.Equal(new[] { ("ggn-check-digit", "certificates[1].number") }, saved.Value.Warnings.Select(w => (w.Code, w.Field)));
    }

    [Fact]
    public async Task Wrong_Numbers_And_Missing_Fields_Are_Refused_A_Ggn_Without_Products_Is_A_Warning()
    {
        var seed = await SeedAsync();
        var refused = await SaveAsync(seed.VendorRelation, "0",
            Ggn("404992841568", null),
            Ggn("40499284156A4", null),
            new SaveCertificateDto { TypeCode = "Unknown", Number = "1" },
            new SaveCertificateDto { TypeCode = CertificateRules.Custom, Number = "1" },
            new SaveCertificateDto { TypeCode = "Skal", Number = "  ", ValidFrom = Today, ValidUntil = Today.AddDays(-1) },
            new SaveCertificateDto { TypeCode = "Skal", Number = "NL-BIO-01", CheckedOn = Today.AddDays(1), ProductIds = new() { 9999 } },
            new SaveCertificateDto { TypeCode = "Skal", Number = "nl-bio-01" });
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Equal(new[]
        {
            "certificates[0].number", "certificates[1].number", "certificates[2].typeCode", "certificates[3].customTypeName", "certificates[4].number",
            "certificates[4].validUntil", "certificates[5].checkedOn", "certificates[5].productIds", "certificates[6].number",
        }, refused.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var pending = await SaveAsync(seed.VendorRelation, "0", Ggn("4049928415684", Today.AddDays(90)));
        Assert.Equal(ServiceResultStatus.Ok, pending.Status);
        Assert.True(pending.Value.Certificates.Single().ProductsPending);
        Assert.Equal(new[] { "products-missing" }, pending.Value.Warnings.Select(w => w.Code));

        var filled = await SaveAsync(seed.VendorRelation, pending.Value.Version, Ggn("4049928415684", Today.AddDays(90), seed.AvocadoId).WithId(pending.Value.Certificates[0].Id));
        Assert.False(filled.Value.Certificates.Single().ProductsPending);
        Assert.Empty(filled.Value.Warnings);
    }

    [Fact]
    public async Task Checked_By_Comes_From_The_Signed_In_User_A_Stale_List_Is_A_Conflict_And_A_Certificate_Is_Archived_Not_Removed()
    {
        var seed = await SeedAsync();
        var first = (await SaveAsync(seed.VendorRelation, "0", new SaveCertificateDto { TypeCode = "Skal", Number = "NL-BIO-01", CheckedOn = Today })).Value;
        Assert.Equal(("Anja", Today), (first.Certificates[0].CheckedBy, first.Certificates[0].CheckedOn));

        // Een ander slaat op zonder de datum te wijzigen: wie controleerde blijft Anja.
        _user = "Piet";
        var id = first.Certificates[0].Id;
        var second = (await SaveAsync(seed.VendorRelation, first.Version, new SaveCertificateDto { Id = id, TypeCode = "Skal", Number = "NL-BIO-01", CheckedOn = Today, CertificationBody = "SKAL" })).Value;
        Assert.Equal("Anja", second.Certificates[0].CheckedBy);
        var third = (await SaveAsync(seed.VendorRelation, second.Version, new SaveCertificateDto { Id = id, TypeCode = "Skal", Number = "NL-BIO-01", CheckedOn = Today.AddDays(-1) })).Value;
        Assert.Equal("Piet", third.Certificates[0].CheckedBy);

        Assert.Equal(ServiceResultStatus.Conflict, (await SaveAsync(seed.VendorRelation, first.Version, new SaveCertificateDto { Id = id, TypeCode = "Skal", Number = "X" })).Status);
        var removed = await SaveAsync(seed.VendorRelation, third.Version);
        Assert.Equal(ServiceResultStatus.Validation, removed.Status);
        Assert.Contains("archive", removed.Errors["certificates"].Single());
        var archived = (await SaveAsync(seed.VendorRelation, third.Version, new SaveCertificateDto { Id = id, TypeCode = "Skal", Number = "NL-BIO-01", IsActive = false })).Value;
        Assert.False(archived.Certificates.Single().IsActive);
        Assert.Null(archived.Certificates.Single().CheckedBy);
    }

    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task Uploading_Makes_A_New_Version_Each_Time_And_Never_Replaces_The_Old_One()
    {
        var seed = await SeedAsync();
        var id = (await SaveAsync(seed.VendorRelation, "0", new SaveCertificateDto { TypeCode = "Brcgs", Number = "BRC-1" })).Value.Certificates[0].Id;
        await using (var db = _factory.CreateContext())
        {
            var first = await Service(db).UploadAsync(id, "brc 2026.pdf", Pdf);
            Assert.Equal(ServiceResultStatus.Created, first.Status);
            Assert.Equal((1, "brc 2026.pdf", "Anja", "application/pdf"), (first.Value.Version, first.Value.FileName, first.Value.CreatedBy, first.Value.MediaType));
            var second = await Service(db).UploadAsync(id, "brc 2027.pdf", Pdf);
            Assert.Equal(2, second.Value.Version);
            Assert.Equal(ServiceResultStatus.Validation, (await Service(db).UploadAsync(id, "notes.txt", System.Text.Encoding.ASCII.GetBytes("hello"))).Status);
            Assert.Equal(ServiceResultStatus.Validation, (await Service(db).UploadAsync(id, "empty.pdf", Array.Empty<byte>())).Status);
            Assert.Equal(ServiceResultStatus.NotFound, (await Service(db).UploadAsync(9999, "x.pdf", Pdf)).Status);
            var file = await Service(db).FileAsync(id, first.Value.Id);
            Assert.Equal(Pdf, file!.Value.Bytes);
        }
        var listed = (await GetAsync(seed.VendorRelation)).Certificates.Single().Files;
        Assert.Equal(new[] { 2, 1 }, listed.Select(f => f.Version));
    }
}

internal static class CertificateTestExtensions
{
    public static SaveCertificateDto WithId(this SaveCertificateDto dto, int id)
    {
        dto.Id = id;
        return dto;
    }
}
