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

        // Inkooporder: factuurnummers bij een order en afkeuringen van ontvangen inkoop.
        public DbSet<SupplyCommitmentInvoiceRef> SupplyCommitmentInvoiceRefs { get; set; }
        public DbSet<SupplyRejection> SupplyRejections { get; set; }
        public DbSet<SupplyRejectionLine> SupplyRejectionLines { get; set; }
        public DbSet<SupplyReceiptDeviation> SupplyReceiptDeviations { get; set; }

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
        public DbSet<Relation> Relations { get; set; }
        public DbSet<RelationAddress> RelationAddresses { get; set; }
        public DbSet<RelationContact> RelationContacts { get; set; }
        public DbSet<RelationContactChannel> RelationContactChannels { get; set; }
        public DbSet<VendorGgn> VendorGgns { get; set; }
        public DbSet<PaymentCondition> PaymentConditions { get; set; }
        public DbSet<RelationBankAccount> RelationBankAccounts { get; set; }
        public DbSet<RelationBankAccountEvent> RelationBankAccountEvents { get; set; }

        // Centrale traceability-guard voor elke writer: zie TraceabilityGuard. De varianten zonder parameters
        // van SaveChanges/SaveChangesAsync lopen via deze overloads.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnsureRelations();
            EnsurePaymentConditionsAsync(async: false, CancellationToken.None).GetAwaiter().GetResult();
            EnsureOrderNumbersAsync(async: false, CancellationToken.None).GetAwaiter().GetResult();
            EnsureInvoiceNumberKeys();
            TraceabilityGuard.Validate(this);
            IncrementVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnsureRelations();
            await EnsurePaymentConditionsAsync(async: true, cancellationToken);
            await EnsureOrderNumbersAsync(async: true, cancellationToken);
            EnsureInvoiceNumberKeys();
            await TraceabilityGuard.ValidateAsync(this, cancellationToken);
            IncrementVersions();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Veilige overgang: een nieuwe klant- of leveranciersrol zonder relatie (oudere schrijvers, seeds, tests) krijgt bij het
        // opslaan een eigen relatie uit zijn platte velden. Zo bestaat er nooit een rol zonder onderneming en wordt niets samengevoegd.
        // Wijzigingen aan de spiegelkolommen buiten RelationService om weigert de TraceabilityGuard (regel 15).
        private void EnsureRelations()
        {
            foreach (var entry in ChangeTracker.Entries<Customer>().Where(e => e.State == EntityState.Added && e.Entity.Relation == null && e.Entity.RelationId == 0).ToList())
            {
                var c = entry.Entity;
                var relation = new Relation { Name = c.Name?.Trim() ?? "", VatNumber = Services.RelationRules.NormalizeVat(c.VATNumber), CreatedAt = c.CreatedAt, UpdatedAt = c.CreatedAt };
                Services.RelationService.ApplyLegacyFlatFields(relation, c.BillingAddress, c.ShippingAddress, c.ContactPerson, c.Email, c.Phone);
                c.Relation = relation;
                relation.Customer = c;
                Services.RelationService.SyncMirror(relation);
                Relations.Add(relation);
            }
            foreach (var entry in ChangeTracker.Entries<Vendor>().Where(e => e.State == EntityState.Added && e.Entity.Relation == null && e.Entity.RelationId == 0).ToList())
            {
                var v = entry.Entity;
                var relation = new Relation { Name = v.Name?.Trim() ?? "", VatNumber = Services.RelationRules.NormalizeVat(v.VATNumber), Email = string.IsNullOrWhiteSpace(v.Email) ? null : v.Email.Trim(), CreatedAt = v.CreatedAt, UpdatedAt = v.CreatedAt };
                Services.RelationService.ApplyLegacyFlatFields(relation, v.BillingAddress, null, null, v.Email, null);
                v.Relation = relation;
                relation.Vendor = v;
                Services.RelationService.SyncMirror(relation);
                Relations.Add(relation);
            }
        }

        // Veilige overgang voor betalingscondities: een nieuwe klant- of leveranciersrol zonder conditie (oudere schrijvers, seeds, tests,
        // EnsureRelations) krijgt de conditie die bij haar PaymentTermsDays hoort via PaymentConditionResolver (bestaande "n dagen na
        // factuurdatum" of een nieuwe "{n}D"). Een bestaande rol wordt hier nooit omgezet; regel 16 van de guard bewaakt de spiegel.
        private async Task EnsurePaymentConditionsAsync(bool async, CancellationToken cancellationToken)
        {
            foreach (var entry in ChangeTracker.Entries<Customer>().Where(e => e.State == EntityState.Added && e.Entity.PaymentCondition == null && e.Entity.PaymentConditionId == 0).ToList())
            {
                PaymentConditionResolver.Assign(entry.Entity, async ? await PaymentConditionResolver.ForDaysAsync(this, entry.Entity.PaymentTermsDays, cancellationToken) : PaymentConditionResolver.ForDays(this, entry.Entity.PaymentTermsDays));
            }
            foreach (var entry in ChangeTracker.Entries<Vendor>().Where(e => e.State == EntityState.Added && e.Entity.PaymentCondition == null && e.Entity.PaymentConditionId == 0).ToList())
            {
                PaymentConditionResolver.Assign(entry.Entity, async ? await PaymentConditionResolver.ForDaysAsync(this, entry.Entity.PaymentTermsDays, cancellationToken) : PaymentConditionResolver.ForDays(this, entry.Entity.PaymentTermsDays));
            }
        }

        // Inkoopordernummer voor elke nieuwe toezegging, ongeacht de schrijver (service, seed, tests): volgende vrije volgnummer in het
        // jaar van CreatedAt (Europe/Amsterdam), na de hoogste opgeslagen en de al in deze save toegekende. Een gelijktijdige schrijver kan
        // hetzelfde nummer kiezen; de unieke index weigert dan de tweede (SupplyCommitmentService probeert opnieuw). Besteldatum zonder
        // waarde wordt de aanmaakdatum. Een bestaand nummer wordt hier nooit gewijzigd.
        private async Task EnsureOrderNumbersAsync(bool async, CancellationToken cancellationToken)
        {
            var added = ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var next = new Dictionary<int, int>();
            foreach (var commitment in added.Where(c => c.OrderNumber == null).OrderBy(c => c.CreatedAt))
            {
                var created = BusinessCalendar.DateOf(commitment.CreatedAt);
                if (commitment.OrderedOn == default)
                {
                    commitment.OrderedOn = created;
                }
                var year = created.Year;
                if (!next.TryGetValue(year, out var sequence))
                {
                    var query = SupplyCommitments.AsNoTracking().Where(c => c.OrderYear == year).Select(c => (int?)c.OrderSequence);
                    var stored = (async ? await query.MaxAsync(cancellationToken) : query.Max()) ?? 0;
                    var tracked = added.Where(c => c.OrderNumber != null && c.OrderYear == year).Select(c => c.OrderSequence).DefaultIfEmpty(0).Max();
                    sequence = Math.Max(stored, tracked);
                }
                sequence++;
                next[year] = sequence;
                commitment.OrderYear = year;
                commitment.OrderSequence = sequence;
                commitment.OrderNumber = Services.PurchaseOrderRules.FormatNumber(year, sequence);
            }
        }

        // Vergelijkingssleutel van een factuurnummer, ongeacht de schrijver: getrimd en in kleine letters (PurchaseOrderRules.InvoiceNumberKey).
        private void EnsureInvoiceNumberKeys()
        {
            foreach (var entry in ChangeTracker.Entries<SupplyCommitmentInvoiceRef>().Where(e => e.State == EntityState.Added))
            {
                entry.Entity.InvoiceNumberKey = Services.PurchaseOrderRules.InvoiceNumberKey(entry.Entity.InvoiceNumber);
            }
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

            foreach (var entry in ChangeTracker.Entries<PaymentCondition>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(p => p.Version).CurrentValue = entry.Property(p => p.Version).OriginalValue + 1;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }

            foreach (var entry in ChangeTracker.Entries<Relation>().Where(e => e.State == EntityState.Modified))
            {
                entry.Property(r => r.Version).CurrentValue = entry.Property(r => r.Version).OriginalValue + 1;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Gedeelde rolafspraken van klant en leverancier (zie IRoleTerms en RoleTermsRules).
        private static void ConfigureRoleTerms<TRole>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TRole> entity) where TRole : class, IRoleTerms
        {
            entity.Property(r => r.PaymentMethod).IsRequired().HasMaxLength(16).HasDefaultValue(PaymentMethods.BankTransfer);
            entity.Property(r => r.MandateReference).HasMaxLength(35);
            entity.Property(r => r.DeliveryTermKind).HasMaxLength(16);
            entity.Property(r => r.Incoterm).HasMaxLength(3);
            entity.Property(r => r.IncotermPlace).HasMaxLength(100);
            entity.Property(r => r.DeliveryTermText).HasMaxLength(200);
            entity.Property(r => r.TradeConditions).HasMaxLength(16);
            entity.Property(r => r.TradeConditionsText).HasMaxLength(200);
        }

        private static void AddRoleTermChecks(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder table, string name)
        {
            table.HasCheckConstraint($"CK_{name}_PaymentMethod_Allowed", "\"PaymentMethod\" IN ('BankTransfer', 'DirectDebit', 'Cash', 'Offset')");
            table.HasCheckConstraint($"CK_{name}_DeliveryTermKind_Allowed", "\"DeliveryTermKind\" IS NULL OR \"DeliveryTermKind\" IN ('Incoterm', 'Franco', 'AfKwekerij', 'Other')");
            table.HasCheckConstraint($"CK_{name}_TradeConditions_Allowed", "\"TradeConditions\" IS NULL OR \"TradeConditions\" IN ('Own', 'Frugiventa', 'Cofreurop', 'Other')");
        }

        // Palletinvoer op aanvoer- en orderregels (spiegel van PackagingRules): alles leeg, of pallets en per pallet positief met
        // losse verpakkingen leeg of heel en >= 0, en het totaal exact pallets × per pallet + los (3 decimalen).
        private static string PalletInputCheck(string quantityColumn) =>
            quantityColumn is not ("Quantity" or "ExpectedQuantity")
                ? throw new ArgumentOutOfRangeException(nameof(quantityColumn), quantityColumn, "Onbekende hoeveelheidskolom.")
                : "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL AND \"LoosePackages\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL " +
            "AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 " +
            "AND (\"LoosePackages\" IS NULL OR (CAST(\"LoosePackages\" AS NUMERIC) >= 0 AND CAST(\"LoosePackages\" AS NUMERIC) = round(CAST(\"LoosePackages\" AS NUMERIC)))) " +
            $"AND abs(CAST(\"{quantityColumn}\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC) - coalesce(CAST(\"LoosePackages\" AS NUMERIC), 0)) < 0.001)";

        // Kg op aanvoer- en orderregels (spiegel van LineWeightRules): netto en bruto leeg of positief, bruto nooit onder netto, en een
        // bron (Calculated of Entered) precies als er kg is.
        private static void LineWeightChecks(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder table, string name, string netColumn, string grossColumn)
        {
            table.HasCheckConstraint($"CK_{name}_Weights_Positive",
                $"(\"{netColumn}\" IS NULL OR CAST(\"{netColumn}\" AS NUMERIC) > 0) AND (\"{grossColumn}\" IS NULL OR CAST(\"{grossColumn}\" AS NUMERIC) > 0)");
            table.HasCheckConstraint($"CK_{name}_Weights_GrossNotBelowNet",
                $"\"{netColumn}\" IS NULL OR \"{grossColumn}\" IS NULL OR CAST(\"{grossColumn}\" AS NUMERIC) >= CAST(\"{netColumn}\" AS NUMERIC)");
            table.HasCheckConstraint($"CK_{name}_WeightSource_Consistent",
                $"(\"WeightSource\" IS NULL AND \"{netColumn}\" IS NULL AND \"{grossColumn}\" IS NULL) " +
                $"OR (\"WeightSource\" IS NOT NULL AND \"WeightSource\" IN ('Calculated', 'Entered') AND (\"{netColumn}\" IS NOT NULL OR \"{grossColumn}\" IS NOT NULL))");
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
                b.Property(c => c.GlobalGapPortalUrl).IsRequired().HasMaxLength(500).HasDefaultValue(RelationRules.DefaultGlobalGapPortalUrl);
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
                b.Property(v => v.DefaultGgn).HasMaxLength(13);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_ProductVariants_WeightMethod_Allowed", "\"WeightMethod\" IN ('Weighed', 'Calculated')");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultNetKgPerPackage_Positive", "\"DefaultNetKgPerPackage\" IS NULL OR CAST(\"DefaultNetKgPerPackage\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultPackagesPerPallet_Positive", "\"DefaultPackagesPerPallet\" IS NULL OR CAST(\"DefaultPackagesPerPallet\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_DefaultGrossKgPerPackage_Positive", "\"DefaultGrossKgPerPackage\" IS NULL OR CAST(\"DefaultGrossKgPerPackage\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_ProductVariants_GoodsCodeSystem_Allowed", "\"GoodsCodeSystem\" IS NULL OR \"GoodsCodeSystem\" IN ('HS', 'CN', 'TARIC')");
                    // GGN: precies 13 tekens; dat het cijfers zijn bewaakt de service (één regel voor SQLite en PostgreSQL).
                    t.HasCheckConstraint("CK_ProductVariants_DefaultGgn_Length", "\"DefaultGgn\" IS NULL OR length(\"DefaultGgn\") = 13");
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

                // Inkoopordernummer: uniek, en per jaar doorlopend (jaar + volgnummer uniek; een botsing bij gelijktijdig
                // aanmaken wordt in de service opnieuw geprobeerd met het volgende nummer).
                b.Property(c => c.OrderNumber).IsRequired().HasMaxLength(20);
                b.HasIndex(c => c.OrderNumber).IsUnique();
                b.HasIndex(c => new { c.OrderYear, c.OrderSequence }).IsUnique();
                b.Property(c => c.SupplierReference).HasMaxLength(PurchaseOrderRules.MaxSupplierReferenceLength);
                b.Property(c => c.OrderChannel).HasMaxLength(16);
                b.Property(c => c.Note).HasMaxLength(PurchaseOrderRules.MaxNoteLength);

                // DirectTrade is altijd klantgebonden; Warehouse-aanbod is nooit aan een klant gebonden.
                b.ToTable(t =>
                {
                    t.HasCheckConstraint(
                        "CK_SupplyCommitments_Route_CustomerLink",
                        "(\"Route\" <> 'DirectTrade' OR \"CustomerId\" IS NOT NULL) AND (\"Route\" <> 'Warehouse' OR \"CustomerId\" IS NULL)");
                    t.HasCheckConstraint("CK_SupplyCommitments_OrderSequence_Positive", "\"OrderSequence\" > 0");
                    t.HasCheckConstraint("CK_SupplyCommitments_OrderChannel_Allowed",
                        "\"OrderChannel\" IS NULL OR \"OrderChannel\" IN ('Phone', 'Email', 'Portal', 'Other')");
                });
            });

            modelBuilder.Entity<SupplyCommitmentInvoiceRef>(b =>
            {
                b.Property(r => r.Kind).IsRequired().HasMaxLength(16);
                b.Property(r => r.InvoiceNumber).IsRequired().HasMaxLength(PurchaseOrderRules.MaxInvoiceNumberLength);
                b.Property(r => r.AmountExclVat).HasPrecision(18, 2);
                b.Property(r => r.ActorReference).HasMaxLength(PurchaseOrderRules.MaxActorLength);
                b.HasIndex(r => r.RequestId).IsUnique();
                // Hetzelfde nummer (zonder hoofdletters, via de sleutel) van dezelfde soort maar één keer per order; opzoeken gaat ook via de sleutel.
                b.Property(r => r.InvoiceNumberKey).IsRequired().HasMaxLength(PurchaseOrderRules.MaxInvoiceNumberLength);
                b.HasIndex(r => new { r.SupplyCommitmentId, r.Kind, r.InvoiceNumberKey }).IsUnique();
                b.HasIndex(r => r.InvoiceNumberKey);
                b.HasOne(r => r.SupplyCommitment).WithMany().HasForeignKey(r => r.SupplyCommitmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(r => r.IncomingInvoice).WithMany().HasForeignKey(r => r.IncomingInvoiceId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SupplyCommitmentInvoiceRefs_Kind_Allowed", "\"Kind\" IN ('Invoice', 'CreditNote')");
                    t.HasCheckConstraint("CK_SupplyCommitmentInvoiceRefs_InvoiceNumber_NotBlank", "length(trim(\"InvoiceNumber\")) > 0");
                    t.HasCheckConstraint("CK_SupplyCommitmentInvoiceRefs_InvoiceNumberKey_NotBlank", "length(trim(\"InvoiceNumberKey\")) > 0");
                });
            });

            modelBuilder.Entity<SupplyRejection>(b =>
            {
                b.Property(r => r.Reason).IsRequired().HasMaxLength(32);
                b.Property(r => r.Explanation).HasMaxLength(PurchaseOrderRules.MaxExplanationLength);
                b.Property(r => r.Decision).IsRequired().HasMaxLength(32);
                b.Property(r => r.ActorReference).IsRequired().HasMaxLength(PurchaseOrderRules.MaxActorLength);
                b.HasIndex(r => r.RequestId).IsUnique();
                b.HasIndex(r => r.SupplyCommitmentId);
                b.HasOne(r => r.SupplyCommitment).WithMany().HasForeignKey(r => r.SupplyCommitmentId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SupplyRejections_Reason_Allowed",
                        "\"Reason\" IN ('Quality', 'Ripeness', 'Temperature', 'Damaged', 'WrongArticle', 'Other')");
                    t.HasCheckConstraint("CK_SupplyRejections_Decision_Allowed",
                        "\"Decision\" IN ('ReturnToSupplier', 'ClaimDiscount', 'Dispose')");
                    t.HasCheckConstraint("CK_SupplyRejections_OtherExplained",
                        "\"Reason\" <> 'Other' OR (\"Explanation\" IS NOT NULL AND length(trim(\"Explanation\")) > 0)");
                });
            });

            // Afwijkende ontvangst (append-only, TraceabilityGuard regel 18): één per ontvangen inhoudsregel en per vervangende regel.
            modelBuilder.Entity<SupplyReceiptDeviation>(b =>
            {
                b.Property(d => d.Kinds).IsRequired().HasMaxLength(100);
                b.Property(d => d.ExpectedVariantSnapshot).IsRequired().HasMaxLength(2000);
                b.Property(d => d.ReceivedVariantSnapshot).IsRequired().HasMaxLength(2000);
                b.Property(d => d.Quantity).HasPrecision(18, 3);
                b.Property(d => d.Unit).IsRequired().HasMaxLength(32);
                b.Property(d => d.ReducedQuantity).HasPrecision(18, 3);
                b.Property(d => d.OriginalLineBefore).IsRequired().HasMaxLength(2000);
                b.Property(d => d.OriginalLineAfter).IsRequired().HasMaxLength(2000);
                b.Property(d => d.Reason).HasMaxLength(32);
                b.Property(d => d.ReasonNote).HasMaxLength(PurchaseOrderRules.MaxExplanationLength);
                b.Property(d => d.ActorReference).HasMaxLength(PurchaseOrderRules.MaxActorLength);
                b.HasIndex(d => d.RequestId).IsUnique();
                b.HasIndex(d => d.SupplyCommitmentId);
                b.HasIndex(d => d.OriginalLineId);
                b.HasIndex(d => d.NewLineId).IsUnique();
                b.HasIndex(d => d.PalletContentLineId).IsUnique();
                b.HasOne(d => d.SupplyCommitment).WithMany().HasForeignKey(d => d.SupplyCommitmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(d => d.OriginalLine).WithMany().HasForeignKey(d => d.OriginalLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(d => d.NewLine).WithMany().HasForeignKey(d => d.NewLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(d => d.PalletContentLine).WithMany().HasForeignKey(d => d.PalletContentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ProductVariant>().WithMany().HasForeignKey(d => d.ExpectedProductVariantId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ProductVariant>().WithMany().HasForeignKey(d => d.ReceivedProductVariantId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_Reduced_Range",
                        "CAST(\"ReducedQuantity\" AS NUMERIC) >= 0 AND CAST(\"ReducedQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC)");
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_Lines_Differ", "\"OriginalLineId\" <> \"NewLineId\"");
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_Variants_Differ", "\"ExpectedProductVariantId\" <> \"ReceivedProductVariantId\"");
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_Reason_Allowed",
                        "\"Reason\" IS NULL OR \"Reason\" IN ('SupplierDeliveredDifferently', 'OtherLotLoaded', 'Other')");
                    t.HasCheckConstraint("CK_SupplyReceiptDeviations_OtherExplained",
                        "\"Reason\" IS NULL OR \"Reason\" <> 'Other' OR (\"ReasonNote\" IS NOT NULL AND length(trim(\"ReasonNote\")) > 0)");
                });
            });

            modelBuilder.Entity<SupplyRejectionLine>(b =>
            {
                b.Property(l => l.Quantity).HasPrecision(18, 3);
                b.Property(l => l.Unit).IsRequired().HasMaxLength(32);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.HasIndex(l => l.PalletContentLineId);
                b.HasOne(l => l.SupplyRejection).WithMany(r => r.Lines).HasForeignKey(l => l.SupplyRejectionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.PalletContentLine).WithMany().HasForeignKey(l => l.PalletContentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(l => l.SupplyCommitmentLine).WithMany().HasForeignKey(l => l.SupplyCommitmentLineId).OnDelete(DeleteBehavior.Restrict);
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SupplyRejectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SupplyRejectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                });
            });

            modelBuilder.Entity<SupplyCommitmentLine>(b =>
            {
                b.Property(l => l.LotNumber).HasMaxLength(100);
                b.Property(l => l.ExpectedQuantity).HasPrecision(18, 3);
                b.Property(l => l.ExpectedUnit).HasMaxLength(32);
                b.Property(l => l.ExpectedNetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.PalletCount).HasPrecision(18, 3);
                b.Property(l => l.PackagesPerPallet).HasPrecision(18, 3);
                b.Property(l => l.LoosePackages).HasPrecision(18, 3);
                b.Property(l => l.ExpectedGrossWeightKg).HasPrecision(18, 3);
                b.Property(l => l.WeightSource).HasMaxLength(16);
                b.Property(l => l.Version).IsConcurrencyToken();

                b.ToTable(t =>
                {
                    // Palletinvoer: alles leeg, of pallets en per pallet positief, losse verpakkingen leeg of heel en >= 0, en ExpectedQuantity
                    // exact pallets × per pallet + los (3 decimalen). Hele pallets alleen in de regels: oudere regels hebben soms een breuk.
                    t.HasCheckConstraint("CK_SupplyCommitmentLines_PalletInput_Consistent", PalletInputCheck("ExpectedQuantity"));
                    LineWeightChecks(t, "SupplyCommitmentLines", "ExpectedNetWeightKg", "ExpectedGrossWeightKg");
                });

                b.HasOne(l => l.SupplyCommitment)
                    .WithMany(c => c.Lines)
                    .HasForeignKey(l => l.SupplyCommitmentId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(l => l.ProductVariant)
                    .WithMany()
                    .HasForeignKey(l => l.ProductVariantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Vervangende regel van een afwijkende ontvangst: verwijst naar de oorspronkelijke regel (nooit verwijderd).
                b.HasOne(l => l.ReplacesLine)
                    .WithMany()
                    .HasForeignKey(l => l.ReplacesLineId)
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
                // Hooguit één klantrol per relatie; de rol verdwijnt nooit stilzwijgend met de relatie (Restrict).
                entity.HasIndex(c => c.RelationId).IsUnique();
                entity.HasOne(c => c.Relation).WithOne(r => r.Customer).HasForeignKey<Customer>(c => c.RelationId).OnDelete(DeleteBehavior.Restrict);
                // Verplichte betalingsconditie; een conditie in gebruik is niet te verwijderen (Restrict), alleen inactief te zetten.
                entity.HasOne(c => c.PaymentCondition).WithMany().HasForeignKey(c => c.PaymentConditionId).OnDelete(DeleteBehavior.Restrict);
                ConfigureRoleTerms(entity);
                entity.Property(c => c.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("EUR");
                entity.Property(c => c.CreditLimit).HasPrecision(18, 2);
                entity.Property(c => c.InsuredLimit).HasPrecision(18, 2);
                entity.Property(c => c.Insurer).HasMaxLength(16);
                entity.Property(c => c.InsurerText).HasMaxLength(100);
                entity.Property(c => c.InsurerReference).HasMaxLength(64);
                entity.ToTable(t =>
                {
                    AddRoleTermChecks(t, "Customers");
                    t.HasCheckConstraint("CK_Customers_Insurer_Allowed", "\"Insurer\" IS NULL OR \"Insurer\" IN ('Atradius', 'AllianzTrade', 'Coface', 'Other')");
                    t.HasCheckConstraint("CK_Customers_CreditLimit_NotNegative", "\"CreditLimit\" IS NULL OR \"CreditLimit\" >= 0");
                    t.HasCheckConstraint("CK_Customers_InsuredLimit_NotNegative", "\"InsuredLimit\" IS NULL OR \"InsuredLimit\" >= 0");
                });
            });

            modelBuilder.Entity<Vendor>(entity =>
            {
                entity.HasIndex(v => v.RelationId).IsUnique();
                entity.HasOne(v => v.Relation).WithOne(r => r.Vendor).HasForeignKey<Vendor>(v => v.RelationId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(v => v.PaymentCondition).WithMany().HasForeignKey(v => v.PaymentConditionId).OnDelete(DeleteBehavior.Restrict);
                ConfigureRoleTerms(entity);
                entity.ToTable(t => AddRoleTermChecks(t, "Vendors"));
            });

            // Bankrekeningen: precies één van IBAN en ander rekeningnummer; hooguit één actieve standaard per relatie; nooit verwijderen
            // (Restrict vanuit de auditregels; de guard weigert een delete).
            modelBuilder.Entity<RelationBankAccount>(b =>
            {
                b.Property(a => a.Iban).HasMaxLength(34);
                b.Property(a => a.OtherAccountNumber).HasMaxLength(64);
                b.Property(a => a.Bic).HasMaxLength(11);
                b.Property(a => a.AccountHolder).IsRequired().HasMaxLength(200);
                b.Property(a => a.Currency).IsRequired().HasMaxLength(3);
                b.Property(a => a.ConfirmationStatus).IsRequired().HasMaxLength(16);
                b.Property(a => a.ConfirmedBy).HasMaxLength(100);
                b.HasOne(a => a.Relation).WithMany(r => r.BankAccounts).HasForeignKey(a => a.RelationId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(a => a.RelationId).IsUnique().HasFilter("\"IsDefault\" AND \"IsActive\"").HasDatabaseName("IX_RelationBankAccounts_RelationId_ActiveDefault");
                b.HasIndex(a => new { a.RelationId, a.SortOrder });
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_RelationBankAccounts_ExactlyOneNumber", "(\"Iban\" IS NULL) <> (\"OtherAccountNumber\" IS NULL)");
                    t.HasCheckConstraint("CK_RelationBankAccounts_ConfirmationStatus_Allowed", "\"ConfirmationStatus\" IN ('Unconfirmed', 'Confirmed')");
                    t.HasCheckConstraint("CK_RelationBankAccounts_AccountHolder_NotBlank", "length(trim(\"AccountHolder\")) > 0");
                });
            });

            modelBuilder.Entity<RelationBankAccountEvent>(b =>
            {
                b.Property(e => e.Kind).IsRequired().HasMaxLength(16);
                b.Property(e => e.ActorReference).IsRequired().HasMaxLength(100);
                b.Property(e => e.OldIban).HasMaxLength(34);
                b.Property(e => e.NewIban).HasMaxLength(34);
                b.Property(e => e.OldAccountHolder).HasMaxLength(200);
                b.Property(e => e.NewAccountHolder).HasMaxLength(200);
                b.Property(e => e.OldOtherAccountNumber).HasMaxLength(64);
                b.Property(e => e.NewOtherAccountNumber).HasMaxLength(64);
                b.HasOne(e => e.BankAccount).WithMany(a => a.Events).HasForeignKey(e => e.RelationBankAccountId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(e => e.Relation).WithMany().HasForeignKey(e => e.RelationId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(e => new { e.RelationId, e.OccurredAt });
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_RelationBankAccountEvents_Kind_Allowed", "\"Kind\" IN ('Added', 'Changed', 'Confirmed', 'Deactivated')");
                    t.HasCheckConstraint("CK_RelationBankAccountEvents_Actor_NotBlank", "length(trim(\"ActorReference\")) > 0");
                });
            });

            // Betalingscondities: code hoofdletterongevoelig uniek via CodeKey; korting beide of geen van beide.
            modelBuilder.Entity<PaymentCondition>(b =>
            {
                b.Property(p => p.Code).IsRequired().HasMaxLength(16);
                b.Property(p => p.CodeKey).IsRequired().HasMaxLength(16);
                b.HasIndex(p => p.CodeKey).IsUnique();
                b.HasIndex(p => p.RequestId).IsUnique().HasFilter("\"RequestId\" IS NOT NULL");
                b.Property(p => p.DescriptionNl).IsRequired().HasMaxLength(200);
                b.Property(p => p.DescriptionEn).IsRequired().HasMaxLength(200);
                b.Property(p => p.Basis).IsRequired().HasMaxLength(16);
                b.Property(p => p.DiscountPercent).HasPrecision(5, 2);
                b.Property(p => p.Version).IsConcurrencyToken();
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_PaymentConditions_Basis_Allowed", "\"Basis\" IN ('InvoiceDate', 'EndOfMonth')");
                    t.HasCheckConstraint("CK_PaymentConditions_Discount_BothOrNeither", "(\"DiscountPercent\" IS NULL) = (\"DiscountDays\" IS NULL)");
                    t.HasCheckConstraint("CK_PaymentConditions_Code_NotBlank", "length(trim(\"Code\")) > 0");
                });
            });

            // Relaties: één wijzigbare bron voor gedeelde bedrijfsgegevens; rollen blijven eigen tabellen met bestaande ID's.
            modelBuilder.Entity<Relation>(b =>
            {
                b.Property(r => r.SearchCode).HasMaxLength(32);
                b.Property(r => r.SearchCodeKey).HasMaxLength(32);
                b.HasIndex(r => r.SearchCodeKey).IsUnique().HasFilter("\"SearchCodeKey\" IS NOT NULL");
                b.HasIndex(r => r.RequestId).IsUnique().HasFilter("\"RequestId\" IS NOT NULL");
                b.Property(r => r.Name).IsRequired().HasMaxLength(200);
                b.Property(r => r.TradeName).HasMaxLength(200);
                b.Property(r => r.PreferredLanguage).HasMaxLength(8);
                b.Property(r => r.Website).HasMaxLength(200);
                b.Property(r => r.Email).HasMaxLength(200);
                b.Property(r => r.Phone).HasMaxLength(40);
                b.Property(r => r.VatNumber).HasMaxLength(32);
                b.Property(r => r.VatVerificationStatus).IsRequired().HasMaxLength(16).HasDefaultValue(VatVerification.NotChecked);
                b.Property(r => r.VatVerifiedNumber).HasMaxLength(32);
                b.Property(r => r.RegistrationNumber).HasMaxLength(64);
                b.Property(r => r.RegistrationCountry).HasMaxLength(2);
                b.Property(r => r.RegistrationType).HasMaxLength(32);
                b.Property(r => r.LegalForm).HasMaxLength(64);
                b.Property(r => r.InternalNote).HasMaxLength(1000);
                b.Property(r => r.Version).IsConcurrencyToken();
                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Relations_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    t.HasCheckConstraint("CK_Relations_VatVerificationStatus_Allowed", "\"VatVerificationStatus\" IN ('NotChecked', 'Valid', 'Invalid', 'Unavailable', 'NotApplicable')");
                });
            });

            modelBuilder.Entity<RelationAddress>(b =>
            {
                b.Property(a => a.Purpose).IsRequired().HasMaxLength(16);
                b.Property(a => a.Label).HasMaxLength(100);
                b.Property(a => a.Line1).HasMaxLength(300);
                b.Property(a => a.Line2).HasMaxLength(200);
                b.Property(a => a.PostalCode).HasMaxLength(20);
                b.Property(a => a.City).HasMaxLength(100);
                b.Property(a => a.Region).HasMaxLength(100);
                b.Property(a => a.CountryCode).HasMaxLength(2);
                b.HasOne(a => a.Relation).WithMany(r => r.Addresses).HasForeignKey(a => a.RelationId).OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(a => new { a.RelationId, a.Purpose });
                b.ToTable(t => t.HasCheckConstraint("CK_RelationAddresses_Purpose_Allowed", "\"Purpose\" IN ('Main', 'Billing', 'Delivery')"));
            });

            modelBuilder.Entity<RelationContact>(b =>
            {
                b.Property(c => c.Name).IsRequired().HasMaxLength(100);
                b.Property(c => c.Department).HasMaxLength(50);
                b.Property(c => c.JobTitle).HasMaxLength(100);
                b.Property(c => c.Note).HasMaxLength(500);
                b.HasOne(c => c.Relation).WithMany(r => r.Contacts).HasForeignKey(c => c.RelationId).OnDelete(DeleteBehavior.Cascade);
                b.ToTable(t => t.HasCheckConstraint("CK_RelationContacts_Name_NotBlank", "length(trim(\"Name\")) > 0"));
            });

            modelBuilder.Entity<RelationContactChannel>(b =>
            {
                b.Property(c => c.Kind).IsRequired().HasMaxLength(8);
                b.Property(c => c.Label).HasMaxLength(50);
                b.Property(c => c.Value).IsRequired().HasMaxLength(200);
                b.HasOne(c => c.RelationContact).WithMany(c => c.Channels).HasForeignKey(c => c.RelationContactId).OnDelete(DeleteBehavior.Cascade);
                b.ToTable(t => t.HasCheckConstraint("CK_RelationContactChannels_Kind_Allowed", "\"Kind\" IN ('Phone', 'Email')"));
            });

            modelBuilder.Entity<VendorGgn>(b =>
            {
                b.Property(g => g.Ggn).IsRequired().HasMaxLength(13);
                b.Property(g => g.ProducerName).HasMaxLength(200);
                b.Property(g => g.CheckedBy).HasMaxLength(100);
                b.HasOne(g => g.Vendor).WithMany(v => v.Ggns).HasForeignKey(g => g.VendorId).OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(g => new { g.VendorId, g.Ggn }).IsUnique();
                b.ToTable(t => t.HasCheckConstraint("CK_VendorGgns_Ggn_Length", "length(\"Ggn\") = 13"));
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
                b.Property(l => l.LoosePackages).HasPrecision(18, 3);
                b.Property(l => l.NetWeightKg).HasPrecision(18, 3);
                b.Property(l => l.GrossWeightKg).HasPrecision(18, 3);
                b.Property(l => l.WeightSource).HasMaxLength(16);
                b.Property(l => l.Version).IsConcurrencyToken();

                // Eén regel per variant + eenheid per order: anders worden reserveringen dubbelzinnig.
                b.HasIndex(l => new { l.SalesOrderId, l.ProductVariantId, l.Unit }).IsUnique();

                b.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_SalesOrderLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    t.HasCheckConstraint("CK_SalesOrderLines_Unit_Allowed", "\"Unit\" IN ('box', 'colli', 'tray', 'piece', 'dozen')");
                    // Palletinvoer: alles leeg, of pallets en per pallet positief, losse verpakkingen leeg of heel en >= 0, en Quantity
                    // exact pallets × per pallet + los (3 decimalen). Hele pallets alleen in de regels: oudere regels hebben soms een breuk.
                    t.HasCheckConstraint("CK_SalesOrderLines_PalletInput_Consistent", PalletInputCheck("Quantity"));
                    LineWeightChecks(t, "SalesOrderLines", "NetWeightKg", "GrossWeightKg");
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
                // Afsplitsing door een gedeeltelijke afkeuring: dezelfde richting (de afgesplitste regel verwijst naar haar afkeuringsregel).
                b.Property(c => c.SplitOffQuantity).HasPrecision(18, 3).HasDefaultValue(0m);
                b.Property(c => c.SplitOffNetWeightKg).HasPrecision(18, 3).HasDefaultValue(0m);
                b.HasIndex(c => c.OriginRejectionLineId).IsUnique();
                b.HasOne(c => c.OriginRejectionLine)
                    .WithOne(l => l.SplitContentLine)
                    .HasForeignKey<PalletContentLine>(c => c.OriginRejectionLineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.Property(c => c.LotNumber).IsRequired().HasMaxLength(100);
                b.Property(c => c.Quantity).HasPrecision(18, 3);
                b.Property(c => c.Unit).IsRequired().HasMaxLength(32);
                b.Property(c => c.NetWeightKg).HasPrecision(18, 3);
                b.Property(c => c.WeightMethod).IsRequired().HasMaxLength(16).HasDefaultValue(WeightRules.Weighed);
                b.Property(c => c.NetKgPerPackage).HasPrecision(18, 3);
                b.Property(c => c.Ggn).HasMaxLength(13);
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
                    t.HasCheckConstraint("CK_PalletContentLines_Ggn_Length", "\"Ggn\" IS NULL OR length(\"Ggn\") = 13");
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
                    // Afgesplitst (afkeuring) is nooit negatief en samen met afgeboekt nooit meer dan de ontvangst.
                    t.HasCheckConstraint("CK_PalletContentLines_SplitOff_Bounds",
                        "CAST(\"SplitOffQuantity\" AS NUMERIC) >= 0 AND CAST(\"SplitOffNetWeightKg\" AS NUMERIC) >= 0 " +
                        "AND CAST(\"DispatchedQuantity\" AS NUMERIC) + CAST(\"SplitOffQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC) " +
                        "AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) + CAST(\"SplitOffNetWeightKg\" AS NUMERIC) <= CAST(\"NetWeightKg\" AS NUMERIC)");
                    // Hooguit één herkomst: teruggeboekt uit een correctie of afgesplitst door een afkeuring, nooit beide.
                    t.HasCheckConstraint("CK_PalletContentLines_SingleOrigin",
                        "\"OriginCorrectionLineId\" IS NULL OR \"OriginRejectionLineId\" IS NULL");
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
