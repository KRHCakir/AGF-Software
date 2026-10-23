using System.Reflection;
using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Interne opmerking per document: alleen op inkoopfacturen (IncomingInvoice) en verkooporders (SalesOrder), alleen voor
// interne werkplekken. Externe uitvoerpaden (klantfactuur Invoice/InvoiceLine + InvoiceDocument-pdf, legacy Order) kennen
// het veld niet, zodat de tekst daar nooit kan verschijnen. Dit is geen toegangsbeveiliging: de applicatie heeft geen
// authenticatie of autorisatie, en elke API-gebruiker kan de interne opmerking lezen.
public class InternalNoteTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Only_Incoming_Invoices_And_Sales_Orders_Carry_An_Internal_Note()
    {
        var carriers = typeof(IncomingInvoice).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(IncomingInvoice).Namespace && t.GetProperty("InternalNote", BindingFlags.Public | BindingFlags.Instance) != null)
            .Select(t => t.Name).OrderBy(n => n).ToArray();
        // Relation: interne opmerking op de relatiekaart (alleen intern zichtbaar, nooit op documenten of naar klanten/leveranciers).
        Assert.Equal(new[] { "IncomingInvoice", "Relation", "SalesOrder" }, carriers);
    }

    [Fact]
    public void External_Documents_And_Their_Builders_Have_No_Internal_Note()
    {
        foreach (var type in new[] { typeof(Invoice), typeof(InvoiceLine), typeof(Order), typeof(OrderLine), typeof(InvoiceDocument), typeof(Customer), typeof(Vendor) })
        {
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.Name.Contains("InternalNote", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(type.GetFields(BindingFlags.Public | BindingFlags.Instance), f => f.Name.Contains("InternalNote", StringComparison.OrdinalIgnoreCase));
        }
        // De klantfactuur-API (SalesInvoiceController) neemt alleen haar eigen velden over; de geneste DTO's kennen geen interne opmerking.
        foreach (var nested in typeof(SalesInvoiceController).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            Assert.DoesNotContain(nested.GetProperties(), p => p.Name.Contains("InternalNote", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Internal_Note_Is_Only_Exposed_Through_The_Internal_Dtos()
    {
        await using var db = _factory.CreateContext();
        var vendor = new Vendor { Name = "The Greenery" };
        var invoice = new IncomingInvoice { Vendor = vendor, InvoiceNumber = "GR-1", Status = "Draft", InternalNote = "Klant komt morgen langs", InvoiceLines = new List<IncomingInvoiceLine>() };
        var order = new SalesOrder { RequestId = Guid.NewGuid(), Customer = new Customer { Name = "Klant" }, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "Jan", InternalNote = "Bellen vóór levering" };
        db.AddRange(invoice, order);
        await db.SaveChangesAsync();

        Assert.Equal("Klant komt morgen langs", (await new PurchaseInvoiceService(db).GetAsync(invoice.Id)).Value.InternalNote);
        Assert.Equal("Bellen vóór levering", (await new SalesOrderService(db).GetAsync(order.Id)).Value.InternalNote);
        Assert.NotNull(typeof(PurchaseInvoiceDto).GetProperty("InternalNote"));
        Assert.NotNull(typeof(SalesOrderDto).GetProperty("InternalNote"));
        // De klantfactuur (Invoice) die uit een legacy Order wordt gemaakt, heeft geen enkel pad naar de interne opmerking.
        Assert.Equal(0, await db.Invoices.CountAsync());
    }
}
