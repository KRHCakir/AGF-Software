using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 1, de overzichten: bijna verlopen (klant en leverancier, het eigen bedrijf), de relatielijst en het GGN-voorstel bij ontvangst.
public partial class CertificateServiceTests
{
    [Fact]
    public async Task The_Overview_Lists_Expiring_And_Expired_Certificates_By_End_Date_For_Customers_And_Suppliers()
    {
        var seed = await SeedAsync();
        await SaveAsync(seed.VendorRelation, "0",
            Ggn("4049928415684", Today.AddDays(31), seed.AvocadoId),
            new SaveCertificateDto { TypeCode = "Skal", Number = "NL-BIO-01", ValidUntil = Today.AddDays(30) },
            new SaveCertificateDto { TypeCode = "Qs", Number = "QS-9", ValidUntil = Today.AddDays(-3), IsActive = false });
        await SaveAsync(seed.CustomerRelation, "0", new SaveCertificateDto { TypeCode = "Brcgs", Number = "BRC-7", ValidUntil = Today.AddDays(-2) });
        await using (var db = _factory.CreateContext())
        {
            await Service(db).SaveOwnAsync(new SaveCertificatesDto { ExpectedVersion = "0", Certificates = new() { new() { TypeCode = "Ifs", Number = "IFS-OWN", ValidUntil = Today.AddDays(5) } } });
            var attention = await Service(db).OverviewAsync(null, null, "attention", false);
            Assert.Equal(30, attention.WarningDays);
            Assert.Equal(new[] { ("BRC-7", -2, true, false), ("IFS-OWN", 5, false, false), ("NL-BIO-01", 30, false, true) },
                attention.Items.Select(i => (i.Number, i.DaysLeft!.Value, i.IsCustomer, i.IsVendor)));
            Assert.Null(attention.Items[1].RelationId);
            Assert.Equal(new[] { "4049928415684" }, (await Service(db).OverviewAsync("4049 9284", null, null, false)).Items.Select(i => i.Number));
            Assert.Equal(new[] { "QS-9" }, (await Service(db).OverviewAsync(null, "Qs", null, true)).Items.Select(i => i.Number));
            Assert.Equal(ServiceResultStatus.Ok, (await Service(db).UpdateSettingsAsync(new CertificateSettingsDto { WarningDays = 60, CustomTypes = new() { "Demeter" } })).Status);
            Assert.Equal(ServiceResultStatus.Validation, (await Service(db).UpdateSettingsAsync(new CertificateSettingsDto { WarningDays = 0 })).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            // 60 dagen vooraf: nu valt ook het GGN (31 dagen) eronder.
            Assert.Equal(4, (await Service(db).OverviewAsync(null, null, "attention", false)).Items.Count);
        }
    }

    [Fact]
    public async Task The_Relation_List_Shows_Certificates_Filters_On_Status_And_Finds_A_Certificate_Number()
    {
        var seed = await SeedAsync();
        await SaveAsync(seed.VendorRelation, "0", Ggn("4049928415684", Today.AddDays(12), seed.AvocadoId), new SaveCertificateDto { TypeCode = "Skal", Number = "NL-BIO-01", ValidUntil = Today.AddDays(400) });
        await using var db = _factory.CreateContext();
        var relations = new RelationService(db);
        var all = await relations.ListAsync("all", null, false, 1, 50);
        var vendor = all.Items.Single(r => r.Id == seed.VendorRelation);
        Assert.Equal(new[] { (CertificateRules.GlobalGapIfa, CertificateRules.StatusExpiringSoon), ("Skal", CertificateRules.StatusValid) }, vendor.Certificates.Select(c => (c.TypeCode, c.Status)));
        Assert.Empty(all.Items.Single(r => r.Id == seed.CustomerRelation).Certificates);
        Assert.Equal(new[] { seed.VendorRelation }, (await relations.ListAsync("all", null, false, 1, 50, certificate: "attention")).Items.Select(r => r.Id));
        Assert.Equal(new[] { seed.CustomerRelation }, (await relations.ListAsync("all", null, false, 1, 50, certificate: "none")).Items.Select(r => r.Id));
        Assert.Equal(new[] { seed.VendorRelation }, (await relations.ListAsync("all", null, false, 1, 50, certificate: "Skal")).Items.Select(r => r.Id));
        Assert.Equal(new[] { seed.VendorRelation }, (await relations.ListAsync("all", "nl-bio", false, 1, 50)).Items.Select(r => r.Id));
    }

    [Fact]
    public async Task The_Receipt_Proposes_The_Active_Ggn_Certificates_And_An_Archived_Certificate_Hides_Its_Old_Field()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        int relationId;
        await using (var db = _factory.CreateContext())
        {
            var vendor = await db.Vendors.SingleAsync(v => v.Id == seed.VendorId);
            relationId = vendor.RelationId;
            db.VendorGgns.Add(new VendorGgn { VendorId = vendor.Id, Ggn = "4049929000001", ProducerName = "Finca Uno" });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(CertificateRules.CopyVendorGgnsSql);
        }
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        async Task<IEnumerable<string>> ProposedAsync()
        {
            await using var db = _factory.CreateContext();
            return (await new SupplyCommitmentService(db).GetAsync(created.Id)).Value.VendorGgns.Select(g => g.Ggn);
        }
        Assert.Equal(new[] { "4049929000001" }, await ProposedAsync());

        var current = await GetAsync(relationId);
        var copy = current.Certificates.Single();
        await SaveAsync(relationId, current.Version,
            new SaveCertificateDto { Id = copy.Id, TypeCode = copy.TypeCode, Number = copy.Number, ProducerName = copy.ProducerName, IsActive = false },
            Ggn("4049928415684", Today.AddDays(100)));
        Assert.Equal(new[] { "4049928415684" }, await ProposedAsync());
    }
}
