// InvoiceDocument.cs (updated for invoice lines)
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AgfAdmin.Models;

public class InvoiceDocument : IDocument
{
    private Invoice Invoice { get; }

    public InvoiceDocument(Invoice invoice)
    {
        Invoice = invoice;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container
            .Page(page =>
            {
                page.Margin(50);
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
    }

    void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text($"Invoice #{Invoice.InvoiceNumber}").FontSize(20).SemiBold();
                column.Item().Text(text =>
                {
                    text.Span("Issue date: ").SemiBold();
                    text.Span(Invoice.InvoiceDate.ToString("dd MMMM yyyy"));
                });
                column.Item().Text(text =>
                {
                    text.Span("Due date: ").SemiBold();
                    text.Span(Invoice.DueDate?.ToString("dd MMMM yyyy") ?? "N/A");
                });
            });

            row.ConstantItem(100).Height(50).Placeholder();
        });
    }

    void ComposeContent(IContainer container)
    {
        container.PaddingVertical(40).Column(column =>
        {
            column.Spacing(20);
            
            // Bill To section
            column.Item().Row(row =>
            {
                row.RelativeItem().Component(new AddressComponent("Bill To", Invoice.Order.Customer.Name, 
                    Invoice.Order.Customer.BillingAddress));
                row.RelativeItem().Component(new AddressComponent("Ship To", Invoice.Order.Customer.Name, 
                    Invoice.Order.Customer.ShippingAddress ?? Invoice.Order.Customer.BillingAddress));
            });

            // Items table
            column.Item().Element(ComposeTable);

            // Totals
            column.Item().PaddingTop(25).AlignRight().Column(totalsColumn =>
            {
                totalsColumn.Item().Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(200).Text("Subtotal:").SemiBold();
                    row.ConstantItem(100).Text(Invoice.SubTotal.ToString("C2")).AlignRight();
                });
                
                totalsColumn.Item().Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(200).Text("VAT:").SemiBold();
                    row.ConstantItem(100).Text(Invoice.VATAmount.ToString("C2")).AlignRight();
                });
                
                totalsColumn.Item().Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(200).Text("Total:").SemiBold();
                    row.ConstantItem(100).Text(Invoice.TotalAmount.ToString("C2")).AlignRight();
                });
            });
        });
    }

    void ComposeTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(25);
                columns.RelativeColumn(3);
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Text("#");
                header.Cell().Text("Description").SemiBold();
                header.Cell().Text("Quantity").SemiBold();
                header.Cell().Text("Unit Price").SemiBold();
                header.Cell().Text("VAT %").SemiBold();
                header.Cell().AlignRight().Text("Total").SemiBold();
            });

            foreach (var (index, item) in Invoice.InvoiceLines.Select((item, index) => (index, item)))
            {
                table.Cell().Element(CellStyle).Text((index + 1).ToString());
                table.Cell().Element(CellStyle).Text(item.Description);
                table.Cell().Element(CellStyle).Text(item.Quantity.ToString());
                table.Cell().Element(CellStyle).Text(item.UnitPrice.ToString("C2"));
                table.Cell().Element(CellStyle).Text($"{item.VATPercentage}%");
                table.Cell().Element(CellStyle).AlignRight().Text(item.Total.ToString("C2"));
            }
        });
    }

    static IContainer CellStyle(IContainer container) => 
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
}

public class AddressComponent : IComponent
{
    private string Title { get; }
    private string Name { get; }
    private string Address { get; }

    public AddressComponent(string title, string name, string address)
    {
        Title = title;
        Name = name;
        Address = address;
    }

    public void Compose(IContainer container)
    {
        container.ShowEntire().Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(Title).SemiBold();
            column.Item().Text(Name);
            column.Item().Text(Address);
        });
    }
}