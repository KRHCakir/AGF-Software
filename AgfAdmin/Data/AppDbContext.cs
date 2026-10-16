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
            });
        }
    }
}
