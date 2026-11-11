using AgfAdmin.Models;
using AgfAdmin.Services.DocumentAi;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    // Tabellen van AI-lezen (stap 7a): de lezingen en de bijlagen bij inkoopfacturen. Een lezing hoort hoogstens bij één factuur en een
    // factuur heeft hoogstens één AI-lezing; een bijlage hoort bij precies één factuur en wordt nooit verwijderd (Restrict).
    internal static class AiReadingModel
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AiExtraction>(b =>
            {
                b.Property(e => e.Status).IsRequired().HasMaxLength(20);
                b.Property(e => e.DocumentType).IsRequired().HasMaxLength(40);
                b.Property(e => e.FileName).IsRequired().HasMaxLength(255);
                b.Property(e => e.MediaType).IsRequired().HasMaxLength(100);
                b.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
                b.Property(e => e.StorageKey).IsRequired().HasMaxLength(200);
                b.Property(e => e.DisplayStorageKey).HasMaxLength(200);
                b.Property(e => e.DisplayMediaType).HasMaxLength(100);
                b.Property(e => e.Provider).IsRequired().HasMaxLength(40);
                b.Property(e => e.Model).HasMaxLength(100);
                b.Property(e => e.ModelVersion).HasMaxLength(100);
                b.Property(e => e.CostUsd).HasPrecision(18, 6);
                b.Property(e => e.EurPerUsd).HasPrecision(18, 6);
                b.Property(e => e.CostEur).HasPrecision(18, 6);
                b.Property(e => e.ErrorCode).HasMaxLength(40);
                b.Property(e => e.ApprovedBy).HasMaxLength(100);
                b.Property(e => e.ClosedBy).HasMaxLength(100);
                b.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
                b.HasIndex(e => e.CreatedAt);
                b.HasIndex(e => e.IncomingInvoiceId).IsUnique();
                b.HasOne(e => e.IncomingInvoice).WithMany().HasForeignKey(e => e.IncomingInvoiceId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_AiExtractions_Status_Allowed", "\"Status\" IN ('Proposed', 'Approved', 'Rejected', 'Manual', 'Failed')");
                    // Goedgekeurd betekent: bij een factuur, door iemand, op een moment; en alleen dan.
                    t.HasCheckConstraint("CK_AiExtractions_Approved_Complete",
                        "(\"Status\" = 'Approved') = (\"IncomingInvoiceId\" IS NOT NULL AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL)");
                    t.HasCheckConstraint("CK_AiExtractions_Costs_NotNegative", "CAST(\"CostUsd\" AS NUMERIC) >= 0 AND CAST(\"CostEur\" AS NUMERIC) >= 0 AND \"InputTokens\" >= 0 AND \"OutputTokens\" >= 0");
                });
            });

            modelBuilder.Entity<DocumentAttachment>(b =>
            {
                b.Property(a => a.FileName).IsRequired().HasMaxLength(255);
                b.Property(a => a.MediaType).IsRequired().HasMaxLength(100);
                b.Property(a => a.Sha256).IsRequired().HasMaxLength(64);
                b.Property(a => a.StorageKey).IsRequired().HasMaxLength(200);
                b.Property(a => a.Source).IsRequired().HasMaxLength(40);
                b.Property(a => a.CreatedBy).IsRequired().HasMaxLength(100);
                b.HasIndex(a => a.IncomingInvoiceId);
                b.HasOne(a => a.IncomingInvoice).WithMany().HasForeignKey(a => a.IncomingInvoiceId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(a => a.AiExtraction).WithMany().HasForeignKey(a => a.AiExtractionId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t => t.HasCheckConstraint("CK_DocumentAttachments_Size_Positive", "\"SizeBytes\" > 0"));
            });
        }
    }
}
