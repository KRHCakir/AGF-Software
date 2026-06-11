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
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class InvoiceController : ControllerBase
    {
        private readonly AppDbContext _context;
        public InvoiceController(AppDbContext context) { _context = context; }

        // 1. De lijst ophalen voor de React pagina
        [HttpGet]
        public async Task<IActionResult> GetInvoices()
        {
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

        // 2. Het PDF Endpoint (Aangeroepen door de Download knop!)
        [HttpGet("{id}/pdf")]
        public async Task<IActionResult> GenerateInvoicePdf(int id)
        {
            // Haal de factuur op inclusief de regels en de klant
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .Include(i => i.Customer)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null) return NotFound("Invoice not found");

            // Definieer de bedrijfsnaam van wie het systeem is (later uit database)
            string companyName = "AGF ERP B.V.";
            string companyAddress = "Handelsweg 1, 1234 AB, Rotterdam";
            string companyEmail = "info@agf-erp.nl";

            // Bouw het document op
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    // HEADER: Logo en Factuurinfo
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            column.Item().Text("INVOICE").FontSize(28).SemiBold().FontColor(Colors.Blue.Darken2);
                            column.Item().Text($"#{invoice.InvoiceNumber}").FontSize(14).FontColor(Colors.Grey.Medium);
                        });

                        row.ConstantItem(175).AlignRight().Column(column =>
                        {
                            column.Item().Text(companyName).Bold().FontSize(12);
                            column.Item().Text(companyAddress);
                            column.Item().Text(companyEmail);
                        });
                    });

                    // CONTENT: Klant, Tabel en Totalen
                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
                    {
                        column.Spacing(20);

                        // Klant Details & Datums
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("Factuur voor:").SemiBold().FontColor(Colors.Grey.Darken2);
                                col.Item().Text(invoice.CustomCustomerName ?? invoice.Customer?.Name ?? "Onbekende Klant").Bold().FontSize(12);
                            });
                            
                            row.ConstantItem(175).AlignRight().Column(col =>
                            {
                                col.Item().Text($"Factuurdatum: {invoice.InvoiceDate:dd-MM-yyyy}");
                                col.Item().Text($"Vervaldatum: {invoice.DueDate:dd-MM-yyyy}");
                            });
                        });

                        // Tabel
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Product Omschrijving
                                columns.RelativeColumn();  // Aantal
                                columns.RelativeColumn();  // Prijs
                                columns.RelativeColumn();  // Totaal
                            });

                            // Tabel Header
                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(5).Text("Omschrijving").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(5).AlignRight().Text("Aantal").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(5).AlignRight().Text("Prijs").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(5).AlignRight().Text("Totaal").SemiBold();
                            });

                            // Tabel Regels (De verkochte producten)
                            foreach (var line in invoice.InvoiceLines)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(line.Description);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text($"{line.Quantity}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text($"€ {line.UnitPrice:0.00}");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text($"€ {(line.Quantity * line.UnitPrice):0.00}");
                            }
                        });

                        // Totaalblok rechtsonder
                        column.Item().AlignRight().Column(col => 
                        {
                            col.Item().Text($"Subtotaal: € {invoice.SubTotal:0.00}");
                            col.Item().Text($"BTW (9%): € {invoice.VATAmount:0.00}");
                            col.Item().PaddingTop(5).Text($"Totaal: € {invoice.TotalAmount:0.00}").Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                        });
                    });

                    // FOOTER: Pagina nummers
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Pagina ");
                        x.CurrentPageNumber();
                        x.Span(" van ");
                        x.TotalPages();
                    });
                });
            });

            // Genereer de PDF en stuur hem naar de browser
            var pdfBytes = document.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
        }
    }
}