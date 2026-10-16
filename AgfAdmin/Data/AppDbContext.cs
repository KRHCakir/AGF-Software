using Microsoft.EntityFrameworkCore;
using AgfAdmin.Models;

namespace AgfAdmin.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Inspection> Inspections { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Inventory> Inventory { get; set; }
        public DbSet<Shipment> Shipments { get; set; }

        // Accounts Payable / Incoming Invoices
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<IncomingInvoice> IncomingInvoices { get; set; }
        public DbSet<IncomingInvoiceLine> IncomingInvoiceLines { get; set; }
        public DbSet<OrderLine> OrderLines { get; set; }
        public DbSet<OrderLineAllocation> OrderLineAllocations { get; set; }
        public DbSet<WasteLog> WasteLogs { get; set; }
        // Voeg deze regel toe aan je bestaande AppDbContext
        public DbSet<CompanySettings> CompanySettings { get; set; }

        // Beschikbaarheidsdomein (verwacht, ontvangen, gereserveerd). Legacy Inventory/Shipment blijven ongewijzigd.
        public DbSet<SupplyCommitment> SupplyCommitments { get; set; }
        public DbSet<SupplyCommitmentLine> SupplyCommitmentLines { get; set; }
        public DbSet<StockReceipt> StockReceipts { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<StockReservation> StockReservations { get; set; }

        // Pallet-traceability: logistieke drager, atomaire inhoudsregels en append-only auditrecords.
        public DbSet<Pallet> Pallets { get; set; }
        public DbSet<PalletContentLine> PalletContentLines { get; set; }
        public DbSet<PalletAuditEvent> PalletAuditEvents { get; set; }

        // Centrale traceability-guard voor elke writer: zie TraceabilityGuard. De varianten zonder parameters
        // van SaveChanges/SaveChangesAsync lopen via deze overloads.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            TraceabilityGuard.Validate(this);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await TraceabilityGuard.ValidateAsync(this, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Alleen de nieuwe entiteiten worden hier geconfigureerd. Alle relaties zijn Restrict:
            // verwijderen van een ouder mag nooit stil audit-/historierecords meenemen.
            // Geen inverse navigaties op legacy-entiteiten, zodat bestaande API-responses gelijk blijven.
            // Hoeveelheden en gewichten: decimal(18,3). Eenheden: max 32 tekens. Alle metadata blijft optioneel
            // tijdens de overgang; er worden geen defaults, conversies of afgeleide gewichten vastgelegd.
            modelBuilder.Entity<ProductVariant>(b =>
            {
                b.Property(v => v.AvailabilityUnit).HasMaxLength(32);
            });

            modelBuilder.Entity<SupplyCommitment>(b =>
            {
                b.Property(c => c.Route).IsRequired().HasMaxLength(32);
                b.Property(c => c.Status).IsRequired().HasMaxLength(32);
                b.Property(c => c.Origin).IsRequired().HasMaxLength(200);
                b.Property(c => c.Destination).HasMaxLength(200);

                b.HasOne(c => c.Vendor)
                    .WithMany()
                    .HasForeignKey(c => c.VendorId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasIndex(c => c.RequestId).IsUnique();

                b.HasOne(c => c.Customer)
                    .WithMany()
                    .HasForeignKey(c => c.CustomerId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(c => c.CustomerId);

                // DirectTrade is altijd klantgebonden; Warehouse-aanbod is nooit aan een klant gebonden.
                b.ToTable(t => t.HasCheckConstraint(
                    "CK_SupplyCommitments_Route_CustomerLink",
                    "(\"Route\" <> 'DirectTrade' OR \"CustomerId\" IS NOT NULL) AND (\"Route\" <> 'Warehouse' OR \"CustomerId\" IS NULL)"));
            });

            modelBuilder.Entity<SupplyCommitmentLine>(b =>
            {
                b.Property(l => l.LotNumber).HasMaxLength(100);
                b.Property(l => l.ExpectedQuantity).HasPrecision(18, 3);
                b.Property(l => l.ExpectedUnit).HasMaxLength(32);
                b.Property(l => l.ExpectedNetWeightKg).HasPrecision(18, 3);

                b.HasOne(l => l.SupplyCommitment)
                    .WithMany(c => c.Lines)
                    .HasForeignKey(l => l.SupplyCommitmentId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.ProductVariant)
                    .WithMany()
                    .HasForeignKey(l => l.ProductVariantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockReceipt>(b =>
            {
                b.HasIndex(r => r.RequestId).IsUnique();

                b.HasOne(r => r.SupplyCommitment)
                    .WithMany()
                    .HasForeignKey(r => r.SupplyCommitmentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockMovement>(b =>
            {
                b.Property(m => m.Type).IsRequired().HasMaxLength(32);
                b.Property(m => m.Reason).HasMaxLength(500);
                b.Property(m => m.Quantity).HasPrecision(18, 3);
                b.Property(m => m.Unit).HasMaxLength(32);
                b.Property(m => m.NetWeightKg).HasPrecision(18, 3);

                b.HasOne(m => m.SupplyCommitmentLine)
                    .WithMany()
                    .HasForeignKey(m => m.SupplyCommitmentLineId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(m => m.StockReceipt)
                    .WithMany()
                    .HasForeignKey(m => m.StockReceiptId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(m => m.StockReservation)
                    .WithMany()
                    .HasForeignKey(m => m.StockReservationId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(m => m.PalletContentLine)
                    .WithMany()
                    .HasForeignKey(m => m.PalletContentLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockReservation>(b =>
            {
                b.Property(r => r.Source).IsRequired().HasMaxLength(32);
                b.Property(r => r.Status).IsRequired().HasMaxLength(32);
                b.Property(r => r.Quantity).HasPrecision(18, 3);
                b.Property(r => r.Unit).HasMaxLength(32);
                b.Property(r => r.NetWeightKg).HasPrecision(18, 3);

                b.HasOne(r => r.OrderLine)
                    .WithMany()
                    .HasForeignKey(r => r.OrderLineId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.SupplyCommitmentLine)
                    .WithMany()
                    .HasForeignKey(r => r.SupplyCommitmentLineId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.PalletContentLine)
                    .WithMany()
                    .HasForeignKey(r => r.PalletContentLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Check-constraints gebruiken SQL met dubbele quotes; geldig in zowel PostgreSQL als SQLite (tests).
            modelBuilder.Entity<Pallet>(b =>
            {
                b.Property(p => p.Reference).IsRequired().HasMaxLength(64);
                b.Property(p => p.Sscc).HasMaxLength(18);
                b.Property(p => p.PalletType).HasMaxLength(32);
                b.Property(p => p.TareKg).HasPrecision(18, 3);
                b.Property(p => p.GrossKg).HasPrecision(18, 3);
                b.Property(p => p.DeclaredNetKg).HasPrecision(18, 3);
                b.Property(p => p.LogisticsStatus).IsRequired().HasMaxLength(32);
                b.Property(p => p.LocationContext).IsRequired().HasMaxLength(32);
                b.Property(p => p.WarehouseLocation).HasMaxLength(64);

                // Referentie nooit leeg; SSCC is NULL of een niet-lege waarde, zodat "" niet als unieke "echte" SSCC telt.
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Pallets_Reference_NotBlank", "length(trim(\"Reference\")) > 0");
                    t.HasCheckConstraint("CK_Pallets_Sscc_NullOrNotBlank", "\"Sscc\" IS NULL OR length(trim(\"Sscc\")) > 0");
                });

                b.HasIndex(p => p.Reference).IsUnique();
                // NULL telt in een unieke index als verschillend (PostgreSQL en SQLite): meerdere pallets zonder SSCC mogen.
                b.HasIndex(p => p.Sscc).IsUnique();

                b.HasOne(p => p.StockReceipt)
                    .WithMany()
                    .HasForeignKey(p => p.StockReceiptId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PalletContentLine>(b =>
            {
                b.Property(c => c.LotNumber).IsRequired().HasMaxLength(100);
                b.Property(c => c.Quantity).HasPrecision(18, 3);
                b.Property(c => c.Unit).IsRequired().HasMaxLength(32);
                b.Property(c => c.NetWeightKg).HasPrecision(18, 3);
                b.Property(c => c.QualityStatus).IsRequired().HasMaxLength(32);
                b.Property(c => c.QualityReason).HasMaxLength(500);

                // CAST AS NUMERIC: exact in PostgreSQL (numeric -> numeric) en dwingt in SQLite, waar EF decimals
                // als TEXT opslaat, een numerieke in plaats van een tekstuele vergelijking af.
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_PalletContentLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_PalletContentLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_PalletContentLines_LotNumber_NotBlank", "length(trim(\"LotNumber\")) > 0");
                    t.HasCheckConstraint("CK_PalletContentLines_Unit_NotBlank", "length(trim(\"Unit\")) > 0");
                });

                b.HasIndex(c => c.RequestId).IsUnique();

                b.HasOne(c => c.Pallet)
                    .WithMany(p => p.ContentLines)
                    .HasForeignKey(c => c.PalletId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(c => c.SupplyCommitmentLine)
                    .WithMany()
                    .HasForeignKey(c => c.SupplyCommitmentLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PalletAuditEvent>(b =>
            {
                b.Property(e => e.EventType).IsRequired().HasMaxLength(64);
                b.Property(e => e.OldValue).HasMaxLength(2000);
                b.Property(e => e.NewValue).HasMaxLength(2000);
                b.Property(e => e.Reason).HasMaxLength(500);
                b.Property(e => e.ActorReference).HasMaxLength(100);

                b.ToTable(t => t.HasCheckConstraint(
                    "CK_PalletAuditEvents_HasTarget",
                    "\"PalletId\" IS NOT NULL OR \"PalletContentLineId\" IS NOT NULL"));

                b.HasOne(e => e.Pallet)
                    .WithMany()
                    .HasForeignKey(e => e.PalletId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(e => e.PalletContentLine)
                    .WithMany()
                    .HasForeignKey(e => e.PalletContentLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
