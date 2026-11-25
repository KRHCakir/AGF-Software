using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, de PDF: een concept live uit de actuele gegevens (met CONCEPT erop, niet bewaard), een definitieve factuur uit de
    // versie die bij Definitief is bewaard (SalesInvoiceDocument). Taal: Engels voor een klant met voorkeur Engels, anders Nederlands.
    public sealed partial class SalesInvoiceService
    {
        public sealed record InvoicePdf(byte[] Bytes, string FileName);

        public async Task<ServiceResult<InvoicePdf>> PdfAsync(int id, CancellationToken cancellationToken = default)
        {
            var invoice = await db.Invoices.AsNoTracking().Where(i => i.Id == id && i.Kind != null).Select(i => new { i.Status }).SingleOrDefaultAsync(cancellationToken);
            if (invoice == null)
            {
                return ServiceResult<InvoicePdf>.NotFound($"Sales invoice {id} does not exist.");
            }
            SalesInvoiceSnapshot snapshot;
            if (invoice.Status == SalesInvoiceRules.StatusFinal)
            {
                var document = await db.SalesInvoiceDocuments.AsNoTracking().Where(d => d.InvoiceId == id).OrderByDescending(d => d.Version).FirstOrDefaultAsync(cancellationToken);
                snapshot = document != null ? SalesInvoiceSnapshotBuilder.Deserialize(document.SnapshotJson) : await SnapshotAsync(id, cancellationToken);
            }
            else
            {
                snapshot = await SnapshotAsync(id, cancellationToken);
            }
            return ServiceResult<InvoicePdf>.Ok(new InvoicePdf(SalesInvoicePdf.Render(snapshot), SalesInvoicePdf.FileName(snapshot)));
        }

        public async Task<ServiceResult<List<SalesInvoiceDocumentDto>>> DocumentsAsync(int id, CancellationToken cancellationToken = default)
        {
            if (!await db.Invoices.AnyAsync(i => i.Id == id && i.Kind != null, cancellationToken))
            {
                return ServiceResult<List<SalesInvoiceDocumentDto>>.NotFound($"Sales invoice {id} does not exist.");
            }
            var documents = await db.SalesInvoiceDocuments.AsNoTracking().Where(d => d.InvoiceId == id).OrderBy(d => d.Version)
                .Select(d => new SalesInvoiceDocumentDto(d.Id, d.Number, d.Version, d.CreatedAt, d.CreatedBy, d.SentAt, d.SentBy)).ToListAsync(cancellationToken);
            return ServiceResult<List<SalesInvoiceDocumentDto>>.Ok(documents);
        }

        private async Task<SalesInvoiceSnapshot> SnapshotAsync(int id, CancellationToken cancellationToken)
        {
            var dto = await DtoAsync(id, cancellationToken);
            var settings = await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var language = await db.Customers.AsNoTracking().Where(c => c.Id == dto.CustomerId).Select(c => c.Relation.PreferredLanguage).FirstOrDefaultAsync(cancellationToken);
            return SalesInvoiceSnapshotBuilder.Build(dto, settings, language);
        }

        // Bij Definitief (in dezelfde transactie, na het nummer): versie 1 van de PDF met de momentopname.
        private async Task<SalesInvoiceDocument> AddFinalDocumentAsync(Invoice invoice, DateTime now, CancellationToken cancellationToken)
        {
            var json = SalesInvoiceSnapshotBuilder.Serialize(await SnapshotAsync(invoice.Id, cancellationToken));
            var document = new SalesInvoiceDocument
            {
                InvoiceId = invoice.Id, Version = 1, Number = invoice.InvoiceNumber, SnapshotJson = json, ContentHash = SalesInvoiceSnapshotBuilder.Hash(json),
                CreatedAt = now, CreatedBy = Actor()
            };
            db.SalesInvoiceDocuments.Add(document);
            return document;
        }
    }
}
