using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Voegt alleen demo-data toe aan lege tabellen; verwijdert of overschrijft nooit bestaande data.
    public class DevelopmentDataSeeder : IDevelopmentDataSeeder
    {
        private const string SeedDemoDataKey = "Development:SeedDemoData";

        // Alleen Development én een expliciete "true"; ontbrekend, false of ongeldig betekent niet seeden.
        public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
        {
            if (!environment.IsDevelopment())
            {
                return false;
            }

            return bool.TryParse(configuration[SeedDemoDataKey], out var enabled) && enabled;
        }

        public async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
        {
            if (!await db.Products.AnyAsync(cancellationToken))
            {
                var p1 = new Product { Name = "Avocado", ImageUrl = "avocado.png" };
                var p2 = new Product { Name = "Mango", ImageUrl = "mango.png" };
                db.Products.AddRange(p1, p2);
                await db.SaveChangesAsync(cancellationToken);

                db.ProductVariants.AddRange(
                    new ProductVariant { ProductId = p1.Id, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m, ShortcutCode = "avohassper22" },
                    new ProductVariant { ProductId = p1.Id, Name = "Green", Origin = "Spain", Packaging = "10kg Crate", DefaultPrice = 22.00m },
                    new ProductVariant { ProductId = p2.Id, Name = "Kent", Origin = "Brazil", Packaging = "4kg Box", DefaultPrice = 9.00m }
                );
                await db.SaveChangesAsync(cancellationToken);
            }

            // Seed Incoming Invoices if empty
            if (!await db.IncomingInvoices.AnyAsync(cancellationToken))
            {
                var vendor = new Vendor { Name = "Example Supplier", Email = "invoices@example.com", VATNumber = "NL88223344B01" };
                db.Vendors.Add(vendor);
                await db.SaveChangesAsync(cancellationToken);

                db.IncomingInvoices.AddRange(
                    new IncomingInvoice
                    {
                        VendorId = vendor.Id,
                        InvoiceNumber = "INV-2024-001",
                        InvoiceDate = DateTime.UtcNow.AddDays(-2),
                        TotalAmount = 1500.00m,
                        VATAmount = 315.00m,
                        Status = "NeedsReview",
                        ReviewConfidence = 0.85,
                        OriginalFilePath = "invoice_001.pdf",
                        InvoiceLines = new List<IncomingInvoiceLine>
                        {
                            new IncomingInvoiceLine { Description = "Consulting Services", Quantity = 1, UnitPrice = 1500, LineTotal = 1500 }
                        }
                    },
                    new IncomingInvoice
                    {
                        VendorId = vendor.Id,
                        InvoiceNumber = "INV-2024-002",
                        InvoiceDate = DateTime.UtcNow.AddDays(-1),
                        TotalAmount = 250.50m,
                        VATAmount = 52.60m,
                        Status = "Processing",
                        ReviewConfidence = 0.45,
                        OriginalFilePath = "scan_123.jpg"
                    }
                );
                await db.SaveChangesAsync(cancellationToken);
            }

            // Seed Customers if empty
            if (!await db.Customers.AnyAsync(cancellationToken))
            {
                db.Customers.Add(new Customer
                {
                    Name = "Local Market Stalls",
                    ContactPerson = "Jan de Vries",
                    Email = "jan@market.nl",
                    BillingAddress = "Markt 1, Amsterdam"
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            // Seed Shipments if empty
            if (!await db.Shipments.AnyAsync(cancellationToken))
            {
                db.Shipments.AddRange(
                    new Shipment { ContainerCode = "MSCU98123", Origin = "Peru", EstimatedTimeOfArrival = DateTime.UtcNow.AddHours(2), Status = "On Truck" },
                    new Shipment { ContainerCode = "MAEU88122", Origin = "Brazil", EstimatedTimeOfArrival = DateTime.UtcNow.AddDays(1), Status = "At Port" },
                    new Shipment { ContainerCode = "CMAU11223", Origin = "Mexico", EstimatedTimeOfArrival = DateTime.UtcNow.AddDays(3), Status = "Sailing" }
                );
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
