using Microsoft.EntityFrameworkCore;
using AgfAdmin.Models;
using AgfAdmin.Services;

namespace AgfAdmin.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            BusinessCalendar = new AgfAdmin.Services.AmsterdamBusinessCalendar(TimeProvider.System);
        }

        // Met DI: dezelfde kalender als de services, zodat service en centrale guard exact dezelfde datumregels toepassen.
        public AppDbContext(DbContextOptions<AppDbContext> options, AgfAdmin.Services.IBusinessCalendar calendar) : base(options)
        {
            BusinessCalendar = calendar ?? new AgfAdmin.Services.AmsterdamBusinessCalendar(TimeProvider.System);
        }

        // Kalender (Europe/Amsterdam) voor de datumcontroles in de TraceabilityGuard.
        public AgfAdmin.Services.IBusinessCalendar BusinessCalendar { get; }

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

        // Schone verkoopflow (los van legacy Order/OrderLine/OrderLineAllocation/Inventory).
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<SalesOrderAuditEvent> SalesOrderAuditEvents { get; set; }
        public DbSet<SalesDispatch> SalesDispatches { get; set; }
        public DbSet<SalesDispatchLine> SalesDispatchLines { get; set; }
        public DbSet<SalesDispatchCorrection> SalesDispatchCorrections { get; set; }
        public DbSet<SalesDispatchCorrectionLine> SalesDispatchCorrectionLines { get; set; }
        public DbSet<DirectTradeShipment> DirectTradeShipments { get; set; }
        public DbSet<DirectTradeShipmentLine> DirectTradeShipmentLines { get; set; }
        public DbSet<DirectTradeShipmentCorrection> DirectTradeShipmentCorrections { get; set; }
        public DbSet<DirectTradeShipmentCorrectionLine> DirectTradeShipmentCorrectionLines { get; set; }
        public DbSet<ShipmentDeliveryConfirmation> ShipmentDeliveryConfirmations { get; set; }
        public DbSet<ShipmentDeliveryRetraction> ShipmentDeliveryRetractions { get; set; }

        // Centrale traceability-guard voor elke writer: zie TraceabilityGuard. De varianten zonder parameters
        // van SaveChanges/SaveChangesAsync lopen via deze overloads.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            TraceabilityGuard.Validate(this);
            IncrementVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await TraceabilityGuard.ValidateAsync(this, cancellationToken);
            IncrementVersions();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Elke gewijzigde pallet of inhoudsregel krijgt versie +1. De oorspronkelijke versie blijft het
        // concurrency-token in de WHERE-clausule, zodat een verouderde schrijver een DbUpdateConcurrencyException krijgt.
        private void IncrementVersions()
        {
            foreach (var entry in ChangeTracker.Entries<Pallet>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(p => p.Version).CurrentValue = entry.Property(p => p.Version).OriginalValue + 1;
            }

            foreach (var entry in ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(c => c.Version).CurrentValue = entry.Property(c => c.Version).OriginalValue + 1;
            }

            foreach (var entry in ChangeTracker.Entries<StockReservation>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(r => r.Version).CurrentValue = entry.Property(r => r.Version).OriginalValue + 1;
            }

            foreach (var entry in ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(o => o.Version).CurrentValue = entry.Property(o => o.Version).OriginalValue + 1;
            }

            foreach (var entry in ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(l => l.Version).CurrentValue = entry.Property(l => l.Version).OriginalValue + 1;
            }

            foreach (var entry in ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(l => l.Version).CurrentValue = entry.Property(l => l.Version).OriginalValue + 1;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Alleen de nieuwe entiteiten worden hier geconfigureerd. Alle relaties zijn Restrict:
            // verwijderen van een ouder mag nooit stil audit-/historierecords meenemen.
            // Geen inverse navigaties op legacy-entiteiten, zodat bestaande API-responses gelijk blijven.
            // Hoeveelheden en gewichten: decimal(18,3). Eenheden: max 32 tekens. Alle metadata blijft optioneel
            // tijdens de overgang; er worden geen defaults, conversies of afgeleide gewichten vastgelegd.
            modelBuilder.Entity<IncomingInvoice>(b =>
            {
                b.Property(i => i.Currency).HasMaxLength(3);
                b.Property(i => i.PaymentReference).HasMaxLength(200);
                b.Property(i => i.Remarks).HasMaxLength(1000);
                b.Property(i => i.ContainerNumber).HasMaxLength(200);
                b.Property(i => i.Transporter).HasMaxLength(200);
                b.Property(i => i.DriverName).HasMaxLength(200);
                b.Property(i => i.LicensePlate).HasMaxLength(32);
                b.Property(i => i.Incoterms).HasMaxLength(32);
                b.Property(i => i.OriginCountry).HasMaxLength(200);
                b.Property(i => i.ReceiptReference).HasMaxLength(200);
                b.Property(i => i.InternalNote).HasMaxLength(1000);
                // NULL telt als verschillend: facturen van vóór de kantoorinvoer (upload, oude handmatige invoer) hebben geen RequestId.
                b.HasIndex(i => i.RequestId).IsUnique();
            });

            modelBuilder.Entity<IncomingInvoiceLine>(b =>
            {
                b.Property(l => l.Unit).HasMaxLength(32);
                b.HasOne(l => l.ProductVariant).WithMany().HasForeignKey(l => l.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CompanySettings>(b =>
            {
                b.Property(c => c.PurchaseInvoiceOptionalFields).HasMaxLength(500).HasDefaultValue(PurchaseInvoiceRules.DefaultOptionalFields);
                b.Property(c => c.PurchaseInvoiceDefaultVatPercentage).HasPrecision(18, 2).HasDefaultValue(PurchaseInvoiceRules.DefaultVatPercentage);
                b.Property(c => c.PurchaseInvoiceDefaultPaymentTermsDays).HasDefaultValue(PurchaseInvoiceRules.DefaultPaymentTermsDays);
                b.Property(c => c.PurchaseInvoiceDefaultCurrency).HasMaxLength(3).HasDefaultValue(PurchaseInvoiceRules.DefaultCurrency);
            });

            modelBuilder.Entity<ProductVariant>(b =>
            {
                b.Property(v => v.AvailabilityUnit).HasMaxLength(32);
                b.Property(v => v.WeightMethod).IsRequired().HasMaxLength(16).HasDefaultValue(WeightRules.Weighed);
                b.Property(v => v.DefaultNetKgPerPackage).HasPrecision(18, 3);
                b.Property(v => v.DefaultPackagesPerPallet).HasPrecision(18, 3);
                // Artikelwerkplek: optionele stamgegevens (alle NULL = niet vastgelegd).
                b.Property(v => v.Brand).HasMaxLength(100);
                b.Property(v => v.PackagingUnit).HasMaxLength(32);
                b.Property(v => v.DefaultGrossKgPerPackage).HasPrecision(18, 3);
                b.Property(v => v.GoodsCode).HasMaxLength(10);
                b.Property(v => v.GoodsCodeSystem).HasMaxLength(8);
                b.Property(v => v.TradeClass).HasMaxLength(50);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_ProductVariants_WeightMethod_Allowed", "\"WeightMethod\" IN ('Weighed', 'Calculated')");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultNetKgPerPackage_Positive", "\"DefaultNetKgPerPackage\" IS NULL OR CAST(\"DefaultNetKgPerPackage\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultPackagesPerPallet_Positive", "\"DefaultPackagesPerPallet\" IS NULL OR CAST(\"DefaultPackagesPerPallet\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultGrossKgPerPackage_Positive", "\"DefaultGrossKgPerPackage\" IS NULL OR CAST(\"DefaultGrossKgPerPackage\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_GoodsCodeSystem_Allowed", "\"GoodsCodeSystem\" IS NULL OR \"GoodsCodeSystem\" IN ('HS', 'CN', 'TARIC')");
                });
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
                b.Property(l => l.PalletCount).HasPrecision(18, 3);
                b.Property(l => l.PackagesPerPallet).HasPrecision(18, 3);
                b.Property(l => l.Version).IsConcurrencyToken();

                // Palletinvoer: beide leeg, of beide positief met ExpectedQuantity exact gelijk aan het product (3 decimalen).
                b.ToTable(t => t.HasCheckConstraint("CK_SupplyCommitmentLines_PalletInput_Consistent",
                    "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL " +
                    "AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 " +
                    "AND abs(CAST(\"ExpectedQuantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)"));

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

                // Gewone FK-index blijft bestaan; daarnaast één Dispatch-mutatie per reservering: dubbel afboeken is op databaseniveau onmogelijk.
                b.HasIndex(m => m.StockReservationId);
                b.HasIndex(m => m.StockReservationId, "IX_StockMovements_Dispatch_StockReservationId")
                    .IsUnique()
                    .HasFilter("\"Type\" = 'Dispatch'");
                // En één compenserende Correction-mutatie per reservering: dubbel terugboeken is evenmin mogelijk.
                b.HasIndex(m => m.StockReservationId, "IX_StockMovements_Correction_StockReservationId")
                    .IsUnique()
                    .HasFilter("\"Type\" = 'Correction'");
                b.HasIndex(m => m.SalesDispatchCorrectionLineId).IsUnique();
                b.HasOne(m => m.SalesDispatchCorrectionLine)
                    .WithOne(l => l.StockMovement)
                    .HasForeignKey<StockMovement>(m => m.SalesDispatchCorrectionLineId)
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
                b.Property(r => r.ReleaseReason).HasMaxLength(500);
                b.Property(r => r.Version).IsConcurrencyToken();

                b.HasIndex(r => r.RequestId).IsUnique();
                b.HasIndex(r => r.ReleaseRequestId).IsUnique();

                // Precies één eigenaar; de verkoopflow is altijd volledig: Physical op een inhoudsregel met gemeten kg, DirectTrade
                // (toewijzing van verwachte, klantgebonden aanvoer) zonder inhoudsregel en zonder kg. Source staat expliciet IS NOT NULL
                // en in een toelatingslijst: een NULL of onbekende bron valt anders in UNKNOWN en zou de check passeren.
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_StockReservations_SingleOwner",
                        "(\"OrderLineId\" IS NOT NULL AND \"SalesOrderLineId\" IS NULL) OR (\"OrderLineId\" IS NULL AND \"SalesOrderLineId\" IS NOT NULL)");
                    t.HasCheckConstraint("CK_StockReservations_Status_Allowed",
                        "\"Status\" IN ('Active', 'Released', 'Consumed', 'Corrected')");
                    t.HasCheckConstraint("CK_StockReservations_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_StockReservations_SalesFlowComplete",
                        "\"SalesOrderLineId\" IS NULL OR (\"RequestId\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"Source\" IS NOT NULL AND \"Source\" IN ('Physical', 'DirectTrade') " +
                        "AND ((\"Source\" = 'Physical' AND \"PalletContentLineId\" IS NOT NULL AND \"NetWeightKg\" IS NOT NULL AND CAST(\"NetWeightKg\" AS NUMERIC) > 0) " +
                        "OR (\"Source\" = 'DirectTrade' AND \"PalletContentLineId\" IS NULL AND \"NetWeightKg\" IS NULL)))");
                    // Een toewijzing kan via een DirectTrade-verzending worden verbruikt en via een DirectTrade-correctie worden teruggenomen.
                    t.HasCheckConstraint("CK_StockReservations_DirectTrade_StatusAllowed",
                        "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released', 'Consumed', 'Corrected')");
                    t.HasCheckConstraint("CK_StockReservations_ReleasedConsistent",
                        "\"Status\" <> 'Released' OR (\"ReleasedAt\" IS NOT NULL AND \"ReleaseReason\" IS NOT NULL AND length(trim(\"ReleaseReason\")) > 0)");
                    t.HasCheckConstraint("CK_StockReservations_ConsumedConsistent",
                        "\"Status\" <> 'Consumed' OR \"ConsumedAt\" IS NOT NULL");
                    t.HasCheckConstraint("CK_StockReservations_CorrectedConsistent",
                        "\"Status\" <> 'Corrected' OR (\"CorrectedAt\" IS NOT NULL AND \"ConsumedAt\" IS NOT NULL)");
                });

                b.HasOne(r => r.OrderLine)
                    .WithMany()
                    .HasForeignKey(r => r.OrderLineId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(r => r.SalesOrderLine)
                    .WithMany(l => l.Reservations)
                    .HasForeignKey(r => r.SalesOrderLineId)
                    .IsRequired(false)
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

            modelBuilder.Entity<Customer>(entity =>
            {
                // Eén klant per client-requestId; oudere klanten zonder requestId blijven buiten de index.
                entity.HasIndex(c => c.RequestId).IsUnique().HasFilter("\"RequestId\" IS NOT NULL");
            });

            modelBuilder.Entity<SalesOrder>(b =>
            {
                b.Property(o => o.Status).IsRequired().HasMaxLength(32);
                b.Property(o => o.CustomerReference).HasMaxLength(100);
                b.Property(o => o.InternalNote).HasMaxLength(1000);
                b.Property(o => o.ActorReference).IsRequired().HasMaxLength(100);
                b.Property(o => o.CancelReason).HasMaxLength(500);
                b.Property(o => o.Version).IsConcurrencyToken();
                b.HasIndex(o => o.RequestId).IsUnique();
                // NULL telt als verschillend: alleen geannuleerde orders hebben een sleutel.
                b.HasIndex(o => o.CancelRequestId).IsUnique();

                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SalesOrders_Status_Allowed", "\"Status\" IN ('Open', 'Cancelled')");
                    // Expliciet IS NOT NULL: een CHECK laat UNKNOWN door, dus NULL-reden of -tijdstip moet apart worden geweigerd.
                    t.HasCheckConstraint("CK_SalesOrders_CancelledConsistent",
                        "\"Status\" <> 'Cancelled' OR (\"CancelledAt\" IS NOT NULL AND \"CancelReason\" IS NOT NULL AND length(trim(\"CancelReason\")) > 0)");
                });

                b.HasOne(o => o.Customer)
                    .WithMany()
                    .HasForeignKey(o => o.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalesOrderLine>(b =>
            {
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                b.Property(l => l.PalletCount).HasPrecision(18, 3);
                b.Property(l => l.PackagesPerPallet).HasPrecision(18, 3);
                b.Property(l => l.Version).IsConcurrencyToken();

                // Eén regel per variant + eenheid per order: anders worden reserveringen dubbelzinnig.
                b.HasIndex(l => new { l.SalesOrderId, l.ProductVariantId, l.Unit }).IsUnique();

                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SalesOrderLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SalesOrderLines_Unit_Allowed", "\"Unit\" IN ('box', 'colli', 'tray', 'piece', 'dozen')");
                    // Palletinvoer: beide leeg, of beide positief met Quantity exact gelijk aan het product (3 decimalen).
                    t.HasCheckConstraint("CK_SalesOrderLines_PalletInput_Consistent",
                        "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL " +
                        "AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 " +
                        "AND abs(CAST(\"Quantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)");
                });

                b.HasOne(l => l.SalesOrder)
                    .WithMany(o => o.Lines)
                    .HasForeignKey(l => l.SalesOrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.ProductVariant)
                    .WithMany()
                    .HasForeignKey(l => l.ProductVariantId)
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

                b.Property(p => p.Version).IsConcurrencyToken();

                // Referentie nooit leeg; SSCC is NULL of een niet-lege waarde, zodat "" niet als unieke "echte" SSCC telt.
                // Toestandsregels binnen één rij: Warehouse alleen met Received, een bin alleen in het magazijn,
                // Delivered alleen bij de klant of extern, en alleen bekende status-/locatiewaarden.
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Pallets_Reference_NotBlank", "length(trim(\"Reference\")) > 0");
                    t.HasCheckConstraint("CK_Pallets_Sscc_NullOrNotBlank", "\"Sscc\" IS NULL OR length(trim(\"Sscc\")) > 0");
                    t.HasCheckConstraint("CK_Pallets_Location_Status",
                        "(\"LocationContext\" <> 'Warehouse' OR \"LogisticsStatus\" = 'Received') " +
                        "AND (\"WarehouseLocation\" IS NULL OR (\"LocationContext\" = 'Warehouse' AND \"LogisticsStatus\" = 'Received' AND length(trim(\"WarehouseLocation\")) > 0))");
                    t.HasCheckConstraint("CK_Pallets_Delivered_Location",
                        "\"LogisticsStatus\" <> 'Delivered' OR \"LocationContext\" IN ('Customer', 'External')");
                    t.HasCheckConstraint("CK_Pallets_LogisticsStatus_Allowed",
                        "\"LogisticsStatus\" IN ('Expected', 'InTransit', 'Received', 'Dispatched', 'Delivered', 'Cancelled')");
                    t.HasCheckConstraint("CK_Pallets_LocationContext_Allowed",
                        "\"LocationContext\" IN ('Supplier', 'Warehouse', 'Crossdock', 'InTransit', 'Customer', 'External')");
                });

                b.HasIndex(p => p.Reference).IsUnique();
                // NULL telt in een unieke index als verschillend (PostgreSQL en SQLite): meerdere pallets zonder SSCC mogen.
                b.HasIndex(p => p.Sscc).IsUnique();
                // Idem voor RequestId: oudere pallets hebben er geen.
                b.HasIndex(p => p.RequestId).IsUnique();

                b.HasOne(p => p.StockReceipt)
                    .WithMany()
                    .HasForeignKey(p => p.StockReceiptId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PalletContentLine>(b =>
            {
                b.Property(c => c.DispatchedQuantity).HasPrecision(18, 3).HasDefaultValue(0m);
                b.Property(c => c.DispatchedNetWeightKg).HasPrecision(18, 3).HasDefaultValue(0m);
                // Eén gezaghebbende richting: de herstelde inhoudsregel verwijst naar haar correctieregel (geen cirkel bij invoegen).
                b.HasIndex(c => c.OriginCorrectionLineId).IsUnique();
                b.HasOne(c => c.OriginCorrectionLine)
                    .WithOne(l => l.RestoredContentLine)
                    .HasForeignKey<PalletContentLine>(c => c.OriginCorrectionLineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.Property(c => c.LotNumber).IsRequired().HasMaxLength(100);
                b.Property(c => c.Quantity).HasPrecision(18, 3);
                b.Property(c => c.Unit).IsRequired().HasMaxLength(32);
                b.Property(c => c.NetWeightKg).HasPrecision(18, 3);
                b.Property(c => c.WeightMethod).IsRequired().HasMaxLength(16).HasDefaultValue(WeightRules.Weighed);
                b.Property(c => c.NetKgPerPackage).HasPrecision(18, 3);
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
                    t.HasCheckConstraint("CK_PalletContentLines_QualityStatus_Allowed",
                        "\"QualityStatus\" IN ('Quarantine', 'Approved', 'Blocked', 'Rejected')");
                    // Gewogen: geen kg per verpakking. Berekend: kg per verpakking > 0 en bij fysieke ontvangst is het totaal exact
                    // aantal × kg per verpakking (afgerond op 3 decimalen, dus verschil < 0,001). Een etiketgewicht kan zo nooit als
                    // gewogen doorgaan. Teruggeboekte correctie-inhoud (OriginCorrectionLineId) draagt de exact verzonden kg; die kan
                    // door opgetelde afronding van deelreserveringen iets afwijken en wordt al door de balansregels (guard) bewaakt.
                    t.HasCheckConstraint("CK_PalletContentLines_WeightMethod_Allowed", "\"WeightMethod\" IN ('Weighed', 'Calculated')");
                    t.HasCheckConstraint("CK_PalletContentLines_Weight_Consistent",
                        "(\"WeightMethod\" = 'Weighed' AND \"NetKgPerPackage\" IS NULL) OR (\"WeightMethod\" = 'Calculated' AND \"NetKgPerPackage\" IS NOT NULL " +
                        "AND CAST(\"NetKgPerPackage\" AS NUMERIC) > 0 AND (\"OriginCorrectionLineId\" IS NOT NULL " +
                        "OR abs(CAST(\"NetWeightKg\" AS NUMERIC) - CAST(\"Quantity\" AS NUMERIC) * CAST(\"NetKgPerPackage\" AS NUMERIC)) < 0.001))");
                    // Afgeboekt ligt tussen 0 en de ontvangst; de ontvangst zelf wordt nooit gewijzigd.
                    t.HasCheckConstraint("CK_PalletContentLines_Dispatched_Bounds",
                        "CAST(\"DispatchedQuantity\" AS NUMERIC) >= 0 AND CAST(\"DispatchedQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC) " +
                        "AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) >= 0 AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) <= CAST(\"NetWeightKg\" AS NUMERIC)");
                });

                b.Property(c => c.Version).IsConcurrencyToken();

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

            modelBuilder.Entity<SalesOrderAuditEvent>(b =>
            {
                b.Property(e => e.EventType).IsRequired().HasMaxLength(64);
                b.Property(e => e.OldValue).HasMaxLength(2000);
                b.Property(e => e.NewValue).HasMaxLength(2000);
                b.Property(e => e.Reason).HasMaxLength(500);
                b.Property(e => e.ActorReference).HasMaxLength(100);

                b.HasIndex(e => e.RequestId).IsUnique();
                b.HasIndex(e => e.SalesOrderId);

                b.HasOne(e => e.SalesOrder)
                    .WithMany()
                    .HasForeignKey(e => e.SalesOrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalesDispatch>(b =>
            {
                b.Property(d => d.Reference).HasMaxLength(100);
                b.Property(d => d.ActorReference).IsRequired().HasMaxLength(100);
                b.HasIndex(d => d.RequestId).IsUnique();
                b.HasIndex(d => d.SalesOrderId);
                b.HasOne(d => d.SalesOrder)
                    .WithMany()
                    .HasForeignKey(d => d.SalesOrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalesDispatchLine>(b =>
            {
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                // Een reservering wordt hooguit één keer verbruikt; elke regel heeft precies één Dispatch-mutatie.
                b.HasIndex(l => l.StockReservationId).IsUnique();
                b.HasIndex(l => l.StockMovementId).IsUnique();
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SalesDispatchLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SalesDispatchLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                });
                b.HasOne(l => l.SalesDispatch)
                    .WithMany(d => d.Lines)
                    .HasForeignKey(l => l.SalesDispatchId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.StockReservation)
                    .WithOne(r => r.DispatchLine)
                    .HasForeignKey<SalesDispatchLine>(l => l.StockReservationId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.SalesOrderLine)
                    .WithMany()
                    .HasForeignKey(l => l.SalesOrderLineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.PalletContentLine)
                    .WithMany()
                    .HasForeignKey(l => l.PalletContentLineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.StockMovement)
                    .WithMany()
                    .HasForeignKey(l => l.StockMovementId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalesDispatchCorrection>(b =>
            {
                b.Property(c => c.Reason).IsRequired().HasMaxLength(500);
                b.Property(c => c.ActorReference).IsRequired().HasMaxLength(100);
                b.HasIndex(c => c.RequestId).IsUnique();
                b.HasIndex(c => c.SalesDispatchId).IsUnique();
                b.HasIndex(c => c.SalesOrderId);
                b.ToTable(t => t.HasCheckConstraint("CK_SalesDispatchCorrections_Confirmed", "\"GoodsNeverLeftWarehouseConfirmed\" = TRUE"));
                b.HasOne(c => c.SalesDispatch)
                    .WithMany()
                    .HasForeignKey(c => c.SalesDispatchId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.SalesOrder)
                    .WithMany()
                    .HasForeignKey(c => c.SalesOrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalesDispatchCorrectionLine>(b =>
            {
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                b.HasIndex(l => l.SalesDispatchLineId).IsUnique();
                b.HasIndex(l => l.StockReservationId).IsUnique();
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SalesDispatchCorrectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SalesDispatchCorrectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                });
                b.HasOne(l => l.SalesDispatchCorrection)
                    .WithMany(c => c.Lines)
                    .HasForeignKey(l => l.SalesDispatchCorrectionId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.SalesDispatchLine)
                    .WithMany()
                    .HasForeignKey(l => l.SalesDispatchLineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.StockReservation)
                    .WithMany()
                    .HasForeignKey(l => l.StockReservationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DirectTradeShipment>(b =>
            {
                b.Property(d => d.CustomerNameSnapshot).IsRequired().HasMaxLength(200);
                b.Property(d => d.Reference).HasMaxLength(100);
                b.Property(d => d.ActorReference).IsRequired().HasMaxLength(100);
                b.HasIndex(d => d.RequestId).IsUnique();
                b.HasIndex(d => d.SalesOrderId);
                b.HasOne(d => d.SalesOrder).WithMany().HasForeignKey(d => d.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t => t.HasCheckConstraint("CK_DirectTradeShipments_CustomerName_NotBlank", "length(trim(\"CustomerNameSnapshot\")) > 0"));
            });

            modelBuilder.Entity<DirectTradeShipmentLine>(b =>
            {
                b.Property(l => l.AllocatedQuantity).HasPrecision(18, 3);
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                b.Property(l => l.LotNumber).IsRequired().HasMaxLength(100);
                b.Property(l => l.SourceLotNumber).HasMaxLength(100);
                b.Property(l => l.LotDeviationReason).HasMaxLength(500);
                b.Property(l => l.VendorNameSnapshot).IsRequired().HasMaxLength(200);
                b.Property(l => l.OriginSnapshot).IsRequired().HasMaxLength(200);
                b.Property(l => l.DestinationSnapshot).HasMaxLength(200);
                // Een toewijzing wordt hooguit één keer verzonden.
                b.HasIndex(l => l.StockReservationId).IsUnique();
                b.HasIndex(l => l.SupplyCommitmentLineId);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_DirectTradeShipmentLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_DirectTradeShipmentLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    // Eerste versie: alleen hele toewijzingen.
                    t.HasCheckConstraint("CK_DirectTradeShipmentLines_WholeAllocation", "CAST(\"Quantity\" AS NUMERIC) = CAST(\"AllocatedQuantity\" AS NUMERIC)");
                    t.HasCheckConstraint("CK_DirectTradeShipmentLines_Lot_NotBlank", "length(trim(\"LotNumber\")) > 0");
                    // Afwijking van een ingevulde bronpartij vraagt een niet-lege toelichting.
                    t.HasCheckConstraint("CK_DirectTradeShipmentLines_LotDeviation_Reason",
                        "\"SourceLotNumber\" IS NULL OR \"LotNumber\" = \"SourceLotNumber\" OR (\"LotDeviationReason\" IS NOT NULL AND length(trim(\"LotDeviationReason\")) > 0)");
                });
                b.HasOne(l => l.DirectTradeShipment).WithMany(d => d.Lines).HasForeignKey(l => l.DirectTradeShipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.StockReservation).WithOne(r => r.DirectTradeShipmentLine).HasForeignKey<DirectTradeShipmentLine>(l => l.StockReservationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.SalesOrderLine).WithMany().HasForeignKey(l => l.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.SupplyCommitmentLine).WithMany().HasForeignKey(l => l.SupplyCommitmentLineId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DirectTradeShipmentCorrection>(b =>
            {
                b.Property(c => c.Reason).IsRequired().HasMaxLength(500);
                b.Property(c => c.ActorReference).IsRequired().HasMaxLength(100);
                b.Property(c => c.ConfirmationCode).IsRequired().HasMaxLength(64);
                b.Property(c => c.ConfirmationStatement).IsRequired().HasMaxLength(200);
                b.HasIndex(c => c.RequestId).IsUnique();
                // Een verzending wordt hooguit één keer teruggenomen.
                b.HasIndex(c => c.DirectTradeShipmentId).IsUnique();
                b.HasIndex(c => c.SalesOrderId);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrections_Confirmed", "\"ShipmentDidNotTakePlaceConfirmed\" = TRUE");
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrections_Code", "\"ConfirmationCode\" = 'SHIPMENT_DID_NOT_TAKE_PLACE'");
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrections_Statement", "\"ConfirmationStatement\" = 'Deze geregistreerde verzending heeft niet plaatsgevonden.'");
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrections_Reason_NotBlank", "length(trim(\"Reason\")) > 0");
                });
                b.HasOne(c => c.DirectTradeShipment).WithOne(s => s.Correction).HasForeignKey<DirectTradeShipmentCorrection>(c => c.DirectTradeShipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.SalesOrder).WithMany().HasForeignKey(c => c.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DirectTradeShipmentCorrectionLine>(b =>
            {
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                b.HasIndex(l => l.DirectTradeShipmentLineId).IsUnique();
                b.HasIndex(l => l.StockReservationId).IsUnique();
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_DirectTradeShipmentCorrectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                });
                b.HasOne(l => l.DirectTradeShipmentCorrection).WithMany(c => c.Lines).HasForeignKey(l => l.DirectTradeShipmentCorrectionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.DirectTradeShipmentLine).WithMany().HasForeignKey(l => l.DirectTradeShipmentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.StockReservation).WithOne(r => r.DirectTradeCorrectionLine).HasForeignKey<DirectTradeShipmentCorrectionLine>(l => l.StockReservationId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ShipmentDeliveryConfirmation>(b =>
            {
                b.Property(c => c.ShipmentKind).IsRequired().HasMaxLength(32);
                b.Property(c => c.ReceiptReference).HasMaxLength(100);
                b.Property(c => c.ActorReference).IsRequired().HasMaxLength(100);
                b.Property(c => c.ConfirmationCode).IsRequired().HasMaxLength(64);
                b.Property(c => c.ConfirmationStatement).IsRequired().HasMaxLength(200);
                b.HasIndex(c => c.RequestId).IsUnique();
                // Volgnummer uniek per verzending en soort. Met de guard (volgnummer = hoogste + 1, alle eerdere ingetrokken) en de orderversie
                // geeft dit hooguit één actieve bevestiging: twee gelijktijdige herbevestigingen kiezen hetzelfde nummer en botsen hier.
                b.HasIndex(c => new { c.SalesDispatchId, c.Sequence }).IsUnique().HasFilter("\"SalesDispatchId\" IS NOT NULL");
                b.HasIndex(c => new { c.DirectTradeShipmentId, c.Sequence }).IsUnique().HasFilter("\"DirectTradeShipmentId\" IS NOT NULL");
                b.HasIndex(c => c.SalesOrderId);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_OneShipment",
                        "(\"ShipmentKind\" = 'Warehouse' AND \"SalesDispatchId\" IS NOT NULL AND \"DirectTradeShipmentId\" IS NULL) " +
                        "OR (\"ShipmentKind\" = 'DirectTrade' AND \"DirectTradeShipmentId\" IS NOT NULL AND \"SalesDispatchId\" IS NULL)");
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_Confirmed", "\"DeliveredInFullConfirmed\" = TRUE");
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_Code", "\"ConfirmationCode\" = 'DELIVERED_IN_FULL'");
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_Statement", "\"ConfirmationStatement\" = 'Deze verzending is volledig en zonder afwijkingen afgeleverd.'");
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_Reference_NullOrNotBlank", "\"ReceiptReference\" IS NULL OR length(trim(\"ReceiptReference\")) > 0");
                    t.HasCheckConstraint("CK_ShipmentDeliveryConfirmations_Sequence_Positive", "\"Sequence\" >= 1");
                });
                b.HasOne(c => c.SalesDispatch).WithMany(d => d.DeliveryConfirmations).HasForeignKey(c => c.SalesDispatchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.DirectTradeShipment).WithMany(s => s.DeliveryConfirmations).HasForeignKey(c => c.DirectTradeShipmentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(c => c.SalesOrder).WithMany().HasForeignKey(c => c.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ShipmentDeliveryRetraction>(b =>
            {
                b.Property(r => r.ShipmentKind).IsRequired().HasMaxLength(32);
                b.Property(r => r.Reason).IsRequired().HasMaxLength(AgfAdmin.Services.ReservationRules.MaxReasonLength);
                b.Property(r => r.ActorReference).IsRequired().HasMaxLength(100);
                b.Property(r => r.ConfirmationCode).IsRequired().HasMaxLength(64);
                b.Property(r => r.ConfirmationStatement).IsRequired().HasMaxLength(200);
                b.HasIndex(r => r.RequestId).IsUnique();
                b.HasIndex(r => r.SalesOrderId);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_ShipmentDeliveryRetractions_OneShipment",
                        "(\"ShipmentKind\" = 'Warehouse' AND \"SalesDispatchId\" IS NOT NULL AND \"DirectTradeShipmentId\" IS NULL) " +
                        "OR (\"ShipmentKind\" = 'DirectTrade' AND \"DirectTradeShipmentId\" IS NOT NULL AND \"SalesDispatchId\" IS NULL)");
                    t.HasCheckConstraint("CK_ShipmentDeliveryRetractions_Confirmed", "\"RecordedInErrorConfirmed\" = TRUE");
                    t.HasCheckConstraint("CK_ShipmentDeliveryRetractions_Code", "\"ConfirmationCode\" = 'DELIVERY_CONFIRMATION_RECORDED_IN_ERROR'");
                    t.HasCheckConstraint("CK_ShipmentDeliveryRetractions_Statement", "\"ConfirmationStatement\" = 'Deze afleverbevestiging is ten onrechte vastgelegd.'");
                    t.HasCheckConstraint("CK_ShipmentDeliveryRetractions_Reason_NotBlank", "length(trim(\"Reason\")) > 0");
                });
                // Hooguit één intrekking per bevestiging: de één-op-één-relatie maakt de FK uniek.
                b.HasOne(r => r.Confirmation).WithOne(c => c.Retraction).HasForeignKey<ShipmentDeliveryRetraction>(r => r.ShipmentDeliveryConfirmationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.SalesOrder).WithMany().HasForeignKey(r => r.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
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

                // NULL telt als verschillend: creatie-events zonder RequestId blijven toegestaan.
                b.HasIndex(e => e.RequestId).IsUnique();

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
