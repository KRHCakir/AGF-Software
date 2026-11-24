using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgfAdmin.Controllers
{
    [Authorize(Policy = Permissions.Sales)]
    [ApiController]
    [Route("api/[controller]")] // Wordt automatisch bereikbaar via /api/SalesInvoice
    [EnableCors("AllowFrontend")]
    public class SalesInvoiceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PartijService _partijen;
        public SalesInvoiceController(AppDbContext context, PartijService partijen) { _context = context; _partijen = partijen; }

        [HttpGet]
        public async Task<IActionResult> GetInvoices()
        {
            // We halen hier EXPLICIET de Verkoopfacturen (Sales) op
            var invoices = await _context.Invoices
                .Include(i => i.Order)
                .OrderByDescending(i => i.InvoiceDate)
                .Select(i => new {
                    i.Id,
                    i.InvoiceNumber,
                    i.OrderId,
                    CustomerName = i.CustomCustomerName ?? (i.Order != null && i.Order.Customer != null ? i.Order.Customer.Name : "Unknown"),
                    i.TotalAmount,
                    i.Status,
                    i.InvoiceDate,
                    i.DueDate,
                    i.CreatedAt
                })
                .ToListAsync();

            return Ok(invoices);
        }

        // Directe factuur (verkoop zonder verkooporder, bijvoorbeeld contant aan de balie). Feedback 22 november: de factuur krijgt een eigen
        // verkoop-partij (het volgende V-nummer, of een eigen nummer dat nog nergens als partij bestaat) en elke regel mag naar de inkoop-partij
        // wijzen waar de goederen uit komen. Een nummer dat al bestaat, van welke soort ook, wordt geweigerd met uitleg (409 PARTIJ_TAKEN).
        [HttpPost]
        public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceDto dto)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? default;
            if (dto == null)
            {
                return this.ToActionResult(ServiceResult<object>.Validation(new Dictionary<string, string[]> { ["request"] = new[] { "A request body is required." } }));
            }
            dto.Lines ??= new List<CreateInvoiceLineDto>();
            var lotErrors = await PurchaseLotErrorsAsync(dto.Lines, cancellationToken);
            if (lotErrors.Count > 0)
            {
                return this.ToActionResult(ServiceResult<object>.Validation(lotErrors));
            }
            Customer? customer = null;
            if (dto.CustomerId is int customerId)
            {
                customer = await _context.Customers.FindAsync(new object[] { customerId }, cancellationToken);
                if (customer == null)
                {
                    return this.ToActionResult(ServiceResult<object>.Validation(new Dictionary<string, string[]> { ["customerId"] = new[] { $"Customer {customerId} does not exist." } }));
                }
            }

            for (var attempt = 1; ; attempt++)
            {
                var lot = await _partijen.BuildDirectInvoiceLotAsync<object>(dto.BatchNumber, "batchNumber", cancellationToken);
                if (lot.Refused != null)
                {
                    return this.ToActionResult(lot.Refused);
                }
                var invoice = BuildInvoice(dto, customer, lot.Partij);
                _context.Invoices.Add(invoice);
                _context.Partijen.Add(lot.Partij);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return Ok(invoice);
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < PartijService.MaxNumberAttempts)
                {
                    // Een gelijktijdige schrijver koos hetzelfde V-nummer: opnieuw met het volgende.
                    _context.ChangeTracker.Clear();
                }
            }
        }

        // Elke opgegeven partij op een regel bestaat en is een inkoop-partij.
        private async Task<Dictionary<string, string[]>> PurchaseLotErrorsAsync(List<CreateInvoiceLineDto> lines, CancellationToken cancellationToken)
        {
            var ids = lines.Where(l => l.PartijId != null).Select(l => l.PartijId.Value).Distinct().ToList();
            var kinds = await _context.Partijen.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Kind, cancellationToken);
            var errors = new Dictionary<string, string[]>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].PartijId is not int id) continue;
                if (!kinds.TryGetValue(id, out var kind))
                {
                    errors[$"lines[{i}].partijId"] = new[] { $"Lot {id} does not exist." };
                }
                else if (kind != PartijRules.KindPurchase)
                {
                    errors[$"lines[{i}].partijId"] = new[] { "A line of a direct invoice comes from a purchase lot, not from a sales lot." };
                }
            }
            return errors;
        }

        private static Invoice BuildInvoice(CreateInvoiceDto dto, Customer customer, Partij salesLot)
        {
            decimal subTotal = dto.Lines.Sum(l => l.Quantity * l.UnitPrice);
            decimal vatAmount = dto.Lines.Sum(l => l.Quantity * l.UnitPrice * (l.VATPercentage / 100m));

            var invoice = new Invoice
            {
                InvoiceNumber = $"FAC-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                InvoiceDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(customer?.PaymentTermsDays > 0 ? customer.PaymentTermsDays : 14),
                CustomerId = dto.CustomerId,
                CustomCustomerName = customer?.Name ?? dto.CustomerName,
                Status = "Nieuw",
                PaymentStatus = "Open",
                SubTotal = subTotal,
                VATAmount = vatAmount,
                TotalAmount = subTotal + vatAmount,
                
                // --- HIER WORDEN DE NIEUWE HEADER VELDEN OPGESLAGEN ---
                DeliveryDate = dto.DeliveryDate ?? DateTime.UtcNow,
                // Het nummer van de eigen verkoop-partij (feedback 22 november), niet meer vrij.
                BatchNumber = salesLot.Number,
                SalesPartij = salesLot,
                DeliveryAddress = dto.DeliveryAddress ?? customer?.BillingAddress,
                ShippingMethod = dto.ShippingMethod,
                ReferenceNumber = dto.ReferenceNumber,
                
                // --- HIER WORDEN DE NIEUWE REGEL VELDEN OPGESLAGEN ---
                InvoiceLines = dto.Lines.Select(l => new InvoiceLine
                {
                    Description = l.Description,
                    Quantity = l.Quantity,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    TareWeight = l.TareWeight,
                    NetWeight = l.NetWeight,
                    OriginCountry = l.OriginCountry,
                    QualityClass = l.QualityClass,
                    DiscountPercentage = l.DiscountPercentage,
                    Remarks = l.Remarks,
                    VATPercentage = l.VATPercentage,
                    PartijId = l.PartijId
                }).ToList()
            };
            salesLot.Invoice = invoice;
            return invoice;
        }


        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GenerateInvoicePdf(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .Include(i => i.Customer)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null) return NotFound("Invoice not found");

            // 1. HAAL JOUW OPGESLAGEN INSTELLINGEN OP UIT DE DATABASE
            var settings = await _context.CompanySettings.FirstOrDefaultAsync();

            string companyName = settings?.CompanyName ?? "AGF ERP B.V.";
            string companyAddress = settings?.Address ?? "Handelsweg 1\n1234 AB Rotterdam\nNederland";
            string companyEmail = settings?.Email ?? "info@agf-erp.nl";
            string companyPhone = settings?.Phone ?? "+31 (0)10 123 4567";
            string companyKvk = !string.IsNullOrWhiteSpace(settings?.KvkNumber) ? $"KVK: {settings.KvkNumber}" : "KVK: 12345678";
            string companyBtw = !string.IsNullOrWhiteSpace(settings?.VatNumber) ? $"BTW: {settings.VatNumber}" : "BTW: NL123456789B01";
            string companyIban = settings?.Iban ?? "NL99 INGB 0123 4567 89";

            // 2. DYNAMISCHE BETAALTERMIJN (AGF Stijl)
            // We kijken in de database hoeveel dagen deze specifieke klant heeft gekregen. 
            // Heeft de klant geen specifieke afspraak? Dan vallen we terug op 14 dagen.
            int paymentDays = invoice.Customer?.PaymentTermsDays > 0 ? invoice.Customer.PaymentTermsDays : 14;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial).FontColor(Colors.Grey.Darken3));

                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            // 3. LOGO INLADEN! 
                            // We checken of er een LogoPath is opgeslagen én of het bestand echt op de server staat.
                            if (!string.IsNullOrWhiteSpace(settings?.LogoPath) && System.IO.File.Exists(settings.LogoPath))
                            {
                                column.Item().Width(120).Image(settings.LogoPath);
                            }
                            else
                            {
                                // Geen logo? Dan laten we de grijze placeholder zien.
                                column.Item().Width(120).Height(60).Placeholder(); 
                            }
                            
                            column.Item().PaddingTop(10).Text(companyName).Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                            column.Item().Text(companyAddress);
                            column.Item().Text(companyEmail);
                            column.Item().Text(companyPhone);
                            column.Item().PaddingTop(5).Text(companyKvk).FontSize(9);
                            column.Item().Text(companyBtw).FontSize(9);
                        });

                        row.ConstantItem(200).AlignRight().Column(column =>
                        {
                            column.Item().Text("FACTUUR").FontSize(32).Black().FontColor(Colors.Blue.Darken2);
                            
                            column.Item().PaddingTop(10).Table(t => 
                            {
                                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                                
                                t.Cell().Text("Factuurnummer:").SemiBold();
                                t.Cell().AlignRight().Text(invoice.InvoiceNumber).SemiBold();
                                
                                t.Cell().Text("Factuurdatum:");
                                t.Cell().AlignRight().Text($"{invoice.InvoiceDate:dd-MM-yyyy}");
                                
                                t.Cell().Text("Vervaldatum:");
                                t.Cell().AlignRight().Text($"{invoice.DueDate:dd-MM-yyyy}");
                            });
                        });
                    });

                    page.Content().PaddingVertical(1.5f, Unit.Centimetre).Column(column =>
                    {
                        column.Spacing(25);

                        // KLANT GEGEVENS BLOK
                        column.Item().Background(Colors.Grey.Lighten4).Padding(15).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("Gefactureerd aan:").SemiBold().FontColor(Colors.Grey.Medium);
                                
                                // 1. Klantnaam
                                col.Item().Text(invoice.CustomCustomerName ?? invoice.Customer?.Name ?? "Onbekende Klant")
                                    .Bold().FontSize(12).FontColor(Colors.Black);
                                
                                // 2. Adres (Als het leeg is, geven we een waarschuwing op de PDF zodat je weet dat je het moet invullen)
                                string address = invoice.Customer?.BillingAddress;
                                if (!string.IsNullOrWhiteSpace(address))
                                {
                                    col.Item().Text(address);
                                }
                                else
                                {
                                    col.Item().Text("Geen adres opgegeven").Italic().FontColor(Colors.Red.Medium);
                                }

                                // 3. BTW Nummer van de klant (Met een klein beetje ruimte ertussen voor de netheid)
                                string customerVat = invoice.Customer?.VATNumber;
                                if (!string.IsNullOrWhiteSpace(customerVat))
                                {
                                    col.Item().PaddingTop(5).Text($"BTW-nummer: {customerVat}").FontSize(9).FontColor(Colors.Grey.Darken2);
                                }
                            });
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); 
                                columns.RelativeColumn();  
                                columns.RelativeColumn();  
                                columns.RelativeColumn();  
                            });

                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(2).BorderColor(Colors.Blue.Darken2).PaddingBottom(5).Text("Omschrijving").SemiBold().FontColor(Colors.Blue.Darken2);
                                header.Cell().BorderBottom(2).BorderColor(Colors.Blue.Darken2).PaddingBottom(5).AlignRight().Text("Aantal").SemiBold().FontColor(Colors.Blue.Darken2);
                                header.Cell().BorderBottom(2).BorderColor(Colors.Blue.Darken2).PaddingBottom(5).AlignRight().Text("Prijs").SemiBold().FontColor(Colors.Blue.Darken2);
                                header.Cell().BorderBottom(2).BorderColor(Colors.Blue.Darken2).PaddingBottom(5).AlignRight().Text("Totaal").SemiBold().FontColor(Colors.Blue.Darken2);
                            });

                            foreach (var line in invoice.InvoiceLines)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(8).Text(line.Description);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(8).AlignRight().Text($"{line.Quantity}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(8).AlignRight().Text($"€ {line.UnitPrice:0.00}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(8).AlignRight().Text($"€ {(line.Quantity * line.UnitPrice):0.00}");
                            }
                        });

                        column.Item().AlignRight().Width(200).Table(t => 
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            
                            t.Cell().PaddingVertical(2).Text("Subtotaal:");
                            t.Cell().PaddingVertical(2).AlignRight().Text($"€ {invoice.SubTotal:0.00}");
                            
                            t.Cell().PaddingVertical(2).Text("BTW (9%):");
                            t.Cell().PaddingVertical(2).AlignRight().Text($"€ {invoice.VATAmount:0.00}");
                            
                            t.Cell().BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).Text("Totaal:").Bold().FontSize(14).FontColor(Colors.Black);
                            t.Cell().BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).AlignRight().Text($"€ {invoice.TotalAmount:0.00}").Bold().FontSize(14).FontColor(Colors.Black);
                        });
                    });

                    // 4. DE FOOTER MET DYNAMISCHE BETAALTERMIJN
                    page.Footer().Column(col => 
                    {
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        col.Item().PaddingTop(10).AlignCenter().Text(text => 
                        {
                            // Hier gebruiken we de variabele 'paymentDays' die we bovenaan berekend hebben!
                            text.Span($"Wij verzoeken u vriendelijk het totaalbedrag van € {invoice.TotalAmount:0.00} binnen {paymentDays} dagen over te maken op IBAN: ").FontSize(9);
                            text.Span(companyIban).Bold().FontSize(9);
                            text.Span($" t.n.v. {companyName} onder vermelding van factuurnummer {invoice.InvoiceNumber}.").FontSize(9);
                        });
                    });
                });
            });

            var pdfBytes = document.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");}
        public class CreateInvoiceDto
        {
            public int? CustomerId { get; set; }
            public string? CustomerName { get; set; }
            
            // --- AGF Specifieke Header Velden ---
            public DateTime? DeliveryDate { get; set; }
            // Eigen nummer voor de verkoop-partij van deze factuur; leeg = het volgende V-nummer. Nooit gelijk aan een bestaande partij.
            public string? BatchNumber { get; set; }
            public string? DeliveryAddress { get; set; }
            public string? ShippingMethod { get; set; }
            public string? ReferenceNumber { get; set; }
            
            public List<CreateInvoiceLineDto> Lines { get; set; } = new();
        }

        public class CreateInvoiceLineDto
        {
            public string Description { get; set; } = string.Empty;
            public decimal Quantity { get; set; } // Bruto aantal of stuks
            public string Unit { get; set; } = "kg"; // kg, doos, pallet, stuk
            public decimal UnitPrice { get; set; }
            
            // --- AGF Specifieke Regel Velden ---
            public decimal TareWeight { get; set; }
            public decimal NetWeight { get; set; }
            public string? OriginCountry { get; set; }
            public string? QualityClass { get; set; }
            public decimal DiscountPercentage { get; set; }
            public string? Remarks { get; set; }
            
            public decimal VATPercentage { get; set; }

            // De inkoop-partij waar deze regel uit komt (optioneel); vast na aanmaken.
            public int? PartijId { get; set; }
        }
    }
}