using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    public interface ISalesParserService
    {
        Task<ParseOrderResponse> ParseOrderTextAsync(string rawText);
    }
}
