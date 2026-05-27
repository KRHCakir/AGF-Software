using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    public interface IEmailIngestionService
    {
        Task CheckForNewEmailsAsync();
        Task<IncomingInvoice> ProcessEmailAttachmentAsync(string filePath, string senderEmail);
    }
}
