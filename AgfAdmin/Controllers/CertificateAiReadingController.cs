using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Certificaten deel 2, onder Stamgegevens: een certificaat (PDF of foto) uploaden en door AI laten lezen (het voorstel), het document
    // bekijken en het voorstel afwijzen of handmatig verder gaan. Goedkeuren gaat via PUT api/certificates/relations/{id} of own met
    // aiApproval (het gewone opslagpad). De status (aan, sleutel, maandlimiet) is dezelfde als bij de inkoopfactuur.
    [ApiController]
    [Route("api/certificates/ai")]
    [Authorize(Policy = Permissions.MasterData)]
    public sealed class CertificateAiReadingController(AiInvoiceReadingService service) : ControllerBase
    {
        private const long MaxRequestBytes = 32 * 1024 * 1024;

        [HttpGet("status")]
        public async Task<IActionResult> Status() => Ok(await service.StatusAsync(HttpContext?.RequestAborted ?? default));

        // relationId leeg = het eigen bedrijf.
        [HttpPost("extractions")]
        [Authorize(Policy = Permissions.MasterDataWrites)]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult> Read(IFormFile file, [FromForm] int? relationId)
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
            return this.ToActionResult(await service.ReadCertificateAsync(relationId, file?.FileName, bytes, cancellationToken));
        }

        [HttpGet("extractions/{id:int}")]
        public async Task<IActionResult> Get(int id) => this.ToActionResult(await service.GetCertificateAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpGet("extractions/{id:int}/document")]
        public async Task<IActionResult> Document(int id)
        {
            var document = await service.CertificateDocumentAsync(id, HttpContext?.RequestAborted ?? default);
            if (document is not { } found) return NotFound(new { message = $"The document of AI reading {id} is not available." });
            Response.Headers.CacheControl = "private, no-store";
            return File(found.Bytes, found.MediaType);
        }

        [HttpPost("extractions/{id:int}/close")]
        [Authorize(Policy = Permissions.MasterDataWrites)]
        public async Task<IActionResult> Close(int id, [FromBody] CloseAiExtractionDto request) =>
            this.ToActionResult(await service.CloseCertificateAsync(id, request, HttpContext?.RequestAborted ?? default));
    }
}
