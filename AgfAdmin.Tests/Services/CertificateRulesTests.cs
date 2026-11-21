using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 1, de basis: de vaste lijst soorten, de controle van een GGN, de status uit de einddatum (op de grens van 30 dagen),
// guard regel 33 en de overname van de oude GGN-velden bij leveranciers op een kleine dataset.
public class CertificateRulesTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private static readonly DateOnly Today = new(2026, 11, 20);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void The_Status_Follows_The_End_Date_With_The_Warning_Days_Inclusive()
    {
        Assert.Equal(CertificateRules.StatusValid, CertificateRules.Status(Today.AddDays(31), Today, 30));
        Assert.Equal(CertificateRules.StatusExpiringSoon, CertificateRules.Status(Today.AddDays(30), Today, 30));
        Assert.Equal(CertificateRules.StatusExpiringSoon, CertificateRules.Status(Today, Today, 30));
        Assert.Equal(CertificateRules.StatusExpired, CertificateRules.Status(Today.AddDays(-1), Today, 30));
        Assert.Equal(CertificateRules.StatusNoEndDate, CertificateRules.Status(null, Today, 30));
        // Een andere instelling per bedrijf: 60 dagen vooraf.
        Assert.Equal(CertificateRules.StatusExpiringSoon, CertificateRules.Status(Today.AddDays(45), Today, 60));
        Assert.Equal(30, CertificateRules.DaysLeft(Today.AddDays(30), Today));
    }

    [Fact]
    public void A_Ggn_Has_13_Digits_And_A_Check_Digit_Other_Numbers_Are_Free()
    {
        Assert.Equal("4049928415684", CertificateRules.NormalizeNumber(CertificateRules.GlobalGapIfa, " 4049 9284 15684 "));
        Assert.True(CertificateRules.HasValidCheckDigit("4049928415684"));
        Assert.False(CertificateRules.HasValidCheckDigit("4049928415685"));
        Assert.False(ArticleRules.IsGgn(CertificateRules.NormalizeNumber(CertificateRules.GlobalGapIfa, "404992841568")));
        Assert.False(ArticleRules.IsGgn(CertificateRules.NormalizeNumber(CertificateRules.GlobalGapIfa, "40499284156A4")));
        // CoC en andere soorten: vrij, ook lang; alleen spaties eromheen gaan weg.
        var coc = "  CoC-NL-2026/000123456789-ABC  ";
        Assert.Equal("CoC-NL-2026/000123456789-ABC", CertificateRules.NormalizeNumber(CertificateRules.GlobalGapCoc, coc));
        Assert.Equal("cocnl2026000123456789abc", CertificateRules.NumberKey(coc));
        Assert.Null(CertificateRules.NumberKey(" - "));
    }

    [Fact]
    public void The_List_Has_The_Usual_Agf_Certificates_And_Only_Ggn_Requires_Products()
    {
        var codes = CertificateRules.Types.Select(t => t.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        foreach (var expected in new[] { "GlobalGapIfa", "GlobalGapCoc", "Grasp", "Smeta", "RainforestAlliance", "Fairtrade", "Skal", "Brcgs", "Ifs", "Siza", "AmforiBsci", "Sa8000", "Qs", "Haccp", "Iso22000", "LeafMarque", "SustainablyGrown" })
        {
            Assert.Contains(expected, codes);
        }
        Assert.Equal(new[] { CertificateRules.GlobalGapIfa }, CertificateRules.Types.Where(t => t.ProductsRequired).Select(t => t.Code));
        Assert.True(CertificateRules.IsKnownType(CertificateRules.Custom));
        Assert.False(CertificateRules.IsKnownType("Unknown"));
        Assert.Equal(new[] { "Demeter", "Eigen audit" }, CertificateRules.ParseCustomTypes("Demeter\n demeter \n\nEigen audit"));
    }

    private static async Task<(int RelationId, int ProductId)> SeedVendorAsync(AppDbContext db, string name, params VendorGgn[] ggns)
    {
        var vendor = new Vendor { Name = name };
        foreach (var ggn in ggns) vendor.Ggns.Add(ggn);
        var product = new Product { Name = $"Avocado {name}" };
        db.Vendors.Add(vendor);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (vendor.RelationId, product.Id);
    }

    [Fact]
    public async Task The_Old_Ggn_Fields_Become_Certificates_Without_Losing_Anything_And_Twice_Is_Safe()
    {
        await using (var db = _factory.CreateContext())
        {
            await SeedVendorAsync(db, "Teler A",
                new VendorGgn { Ggn = "4049928415684", ProducerName = "Finca Uno", SortOrder = 0, CheckedOn = new DateOnly(2026, 10, 1), CheckedBy = "Jan", CertificateValidUntil = new DateOnly(2027, 3, 31), HasGgnLabel = true },
                new VendorGgn { Ggn = "4049929000002", IsActive = false, SortOrder = 1 });
            await SeedVendorAsync(db, "Teler B");
            await db.Database.ExecuteSqlRawAsync(CertificateRules.CopyVendorGgnsSql);
            await db.Database.ExecuteSqlRawAsync(CertificateRules.CopyVendorGgnsSql);
        }

        await using var check = _factory.CreateContext();
        var copied = await check.RelationCertificates.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        Assert.Equal(2, copied.Count);
        var vendorGgns = await check.VendorGgns.AsNoTracking().Include(g => g.Vendor).OrderBy(g => g.SortOrder).ToListAsync();
        Assert.Equal(2, vendorGgns.Count);
        for (var i = 0; i < copied.Count; i++)
        {
            var (old, cert) = (vendorGgns[i], copied[i]);
            Assert.Equal(old.Vendor.RelationId, cert.RelationId);
            Assert.Equal((CertificateRules.GlobalGapIfa, old.Ggn, old.Ggn, old.Id), (cert.TypeCode, cert.Number, cert.NumberKey, cert.SourceVendorGgnId));
            Assert.Equal((old.ProducerName, old.IsActive, old.CheckedOn, old.CheckedBy, old.CertificateValidUntil, old.HasGgnLabel),
                (cert.ProducerName, cert.IsActive, cert.CheckedOn, cert.CheckedBy, cert.ValidUntil, cert.HasGgnLabel));
            Assert.True(cert.ProductsPending);
            Assert.Equal("Migratie", cert.CreatedBy);
        }
        Assert.Empty(await check.RelationCertificateProducts.ToListAsync());
    }

    [Fact]
    public async Task A_Certificate_Is_Never_Removed_And_Stays_With_Its_Relation_Files_And_Confirmations_Are_Append_Only()
    {
        int certificateId;
        int otherRelation;
        await using (var db = _factory.CreateContext())
        {
            var (relationId, productId) = await SeedVendorAsync(db, "Teler C");
            (otherRelation, _) = await SeedVendorAsync(db, "Teler D");
            var certificate = new RelationCertificate { RelationId = relationId, TypeCode = "Skal", Number = "SKAL-123", NumberKey = "skal123", CreatedBy = "Jan", UpdatedBy = "Jan" };
            certificate.Products.Add(new RelationCertificateProduct { ProductId = productId });
            certificate.Files.Add(new RelationCertificateFile { Version = 1, FileName = "skal.pdf", MediaType = "application/pdf", SizeBytes = 10, Sha256 = new string('a', 64), StorageKey = "2026-11/a.pdf", CreatedBy = "Jan" });
            db.RelationCertificates.Add(certificate);
            await db.SaveChangesAsync();
            certificateId = certificate.Id;
        }

        await using (var db = _factory.CreateContext())
        {
            var certificate = await db.RelationCertificates.SingleAsync(c => c.Id == certificateId);
            db.RelationCertificates.Remove(certificate);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var certificate = await db.RelationCertificates.SingleAsync(c => c.Id == certificateId);
            certificate.RelationId = otherRelation;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            // Archiveren en gegevens aanpassen mag.
            var certificate = await db.RelationCertificates.SingleAsync(c => c.Id == certificateId);
            certificate.IsActive = false;
            certificate.ValidUntil = Today;
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var file = await db.RelationCertificateFiles.SingleAsync();
            file.FileName = "anders.pdf";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.RelationCertificateFiles.Remove(await db.RelationCertificateFiles.SingleAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            // Vervangen = een nieuwe versie; een versie overslaan of herhalen kan niet.
            db.RelationCertificateFiles.Add(NewFile(certificateId, 3));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.RelationCertificateFiles.Add(NewFile(certificateId, 2));
            db.RelationCertificateFiles.Add(NewFile(certificateId, 3));
            await db.SaveChangesAsync();
            Assert.Equal(new[] { 1, 2, 3 }, await db.RelationCertificateFiles.Where(f => f.RelationCertificateId == certificateId).OrderBy(f => f.Version).Select(f => f.Version).ToListAsync());
        }
    }

    private static RelationCertificateFile NewFile(int certificateId, int version) => new()
    {
        RelationCertificateId = certificateId, Version = version, FileName = $"v{version}.pdf", MediaType = "application/pdf", SizeBytes = 10,
        Sha256 = new string('b', 64), StorageKey = $"2026-11/{version}.pdf", CreatedBy = "Jan",
    };
}
