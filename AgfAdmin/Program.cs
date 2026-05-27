using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Services registreren
builder.Services.AddControllers()
    .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// AI & Email Services
builder.Services.AddScoped<IEmailIngestionService, EmailIngestionService>();
builder.Services.AddScoped<IAIExtractorService, MockAIExtractorService>();
builder.Services.AddScoped<ISalesParserService, SimpleSalesParserService>();

// Database configuratie
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS configuratie
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy.WithOrigins("http://localhost:3000")  // Pas dit aan naar je frontend-URL
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

// QuestPDF License
QuestPDF.Settings.License = LicenseType.Community;

app.UseCors("AllowFrontend");

// 2. Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

 // Gebruik de default policy
app.UseAuthorization();
app.MapControllers();

// 3. Database seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Use EnsureCreated because dotnet ef tool is having issues on this machine
    // FORCE RESET for Schema Change
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();

    if (!db.Products.Any())
    {
        var p1 = new Product { Name = "Avocado", ImageUrl = "avocado.png" };
        var p2 = new Product { Name = "Mango", ImageUrl = "mango.png" };
        db.Products.AddRange(p1, p2);
        db.SaveChanges();

        db.ProductVariants.AddRange(
            new ProductVariant { ProductId = p1.Id, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m, ShortcutCode = "avohassper22" },
            new ProductVariant { ProductId = p1.Id, Name = "Green", Origin = "Spain", Packaging = "10kg Crate", DefaultPrice = 22.00m },
            new ProductVariant { ProductId = p2.Id, Name = "Kent", Origin = "Brazil", Packaging = "4kg Box", DefaultPrice = 9.00m }
        );
        db.SaveChanges();
    }

    // Seed Incoming Invoices if empty
    if (!db.IncomingInvoices.Any())
    {
        var vendor = new Vendor { Name = "Example Supplier", Email = "invoices@example.com", VATNumber = "NL88223344B01" };
        db.Vendors.Add(vendor);
        db.SaveChanges();

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
        db.SaveChanges();
    }

    // Seed Customers if empty
    if (!db.Customers.Any())
    {
        db.Customers.Add(new Customer 
        { 
            Name = "Local Market Stalls", 
            ContactPerson = "Jan de Vries",
            Email = "jan@market.nl",
            BillingAddress = "Markt 1, Amsterdam"
        });
        db.SaveChanges();
    }

    // Seed Shipments
    if (!db.Shipments.Any())
    {
        db.Shipments.AddRange(
            new Shipment { ContainerCode = "MSCU98123", Origin = "Peru", EstimatedTimeOfArrival = DateTime.UtcNow.AddHours(2), Status = "On Truck" },
            new Shipment { ContainerCode = "MAEU88122", Origin = "Brazil", EstimatedTimeOfArrival = DateTime.UtcNow.AddDays(1), Status = "At Port" },
            new Shipment { ContainerCode = "CMAU11223", Origin = "Mexico", EstimatedTimeOfArrival = DateTime.UtcNow.AddDays(3), Status = "Sailing" }
        );
        db.SaveChanges();
    }
}

app.Run();