using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // AI-lezen van een inkoopfactuur (stap 7a), onder Inkoop: de status, een document uploaden en laten lezen (het voorstel), het document
    // bekijken en het voorstel afwijzen of handmatig verder gaan. Goedkeuren gaat via POST api/purchase-invoices (het gewone opslagpad).
    [ApiController]
    [Route("api/purchase-invoices/ai")]
    [Authorize(Policy = Permissions.Purchase)]
    public sealed class AiInvoiceReadingController(AiInvoiceReadingService service, AiInvoiceMatching matching) : ControllerBase
    {
        // Ruim boven de documentgrens (10 MB, DocumentAi:MaxFileBytes), zodat een te groot bestand een nette melding krijgt.
        private const long MaxRequestBytes = 32 * 1024 * 1024;

        [HttpGet("status")]
        public async Task<IActionResult> Status() => Ok(await service.StatusAsync(HttpContext?.RequestAborted ?? default));

        [HttpPost("extractions")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult> Read(IFormFile file)
        {
            var cancellationToken = HttpContext?.RequestAborted ?? default;
            byte[] bytes = Array.Empty<byte>();
            if (file != null)
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }
            return this.ToActionResult(await WithMatchesAsync(await service.ReadAsync(file?.FileName, bytes, cancellationToken)));
        }

        [HttpGet("extractions/{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await WithMatchesAsync(await service.GetAsync(id, HttpContext?.RequestAborted ?? default)));

        // Koppelen aan leverancier, inkooporder, dubbele factuur en kostenartikel: alleen voorstellen bij het voorstel.
        private async Task<ServiceResult<AiExtractionDto>> WithMatchesAsync(ServiceResult<AiExtractionDto> result)
        {
            if (result.Status == ServiceResultStatus.Ok && result.Value.Proposal != null)
            {
                result.Value.Matches = await matching.MatchAsync(result.Value.Proposal, HttpContext?.RequestAborted ?? default);
            }
            return result;
        }

        [HttpGet("extractions/{id:int}/document")]
        public async Task<IActionResult> Document(int id)
        {
            var document = await service.DocumentAsync(id, HttpContext?.RequestAborted ?? default);
            if (document is not { } found) return NotFound(new { message = $"The document of AI reading {id} is not available." });
            Response.Headers.CacheControl = "private, no-store";
            return File(found.Bytes, found.MediaType);
        }

        [HttpPost("extractions/{id:int}/close")]
        public async Task<IActionResult> Close(int id, [FromBody] CloseAiExtractionDto request) =>
            this.ToActionResult(await service.CloseAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
