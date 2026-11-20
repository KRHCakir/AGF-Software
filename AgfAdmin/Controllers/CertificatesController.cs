using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Certificaten deel 1 (Stamgegevens): de soorten, de certificaten van een relatie of van het eigen bedrijf, hun PDF's en foto's, het
    // overzicht (ook bijna verlopen) en de instellingen. Lezen mag iedereen, wijzigen met Stamgegevens; de instellingen ook met Beheer.
    [ApiController]
    [Route("api/certificates")]
    [Authorize(Policy = Permissions.MasterDataWrites)]
    public sealed class CertificatesController(CertificateService service) : ControllerBase
    {
        // Ruim boven de documentgrens (10 MB, DocumentAi:MaxFileBytes), zodat een te groot bestand een nette melding krijgt.
        private const long MaxRequestBytes = 32 * 1024 * 1024;

        private CancellationToken Aborted => HttpContext?.RequestAborted ?? default;

        [HttpGet("reference")]
        public async Task<IActionResult> Reference() => Ok(await service.ReferenceAsync(Aborted));

        // status: attention (bijna verlopen en verlopen), Expired, ExpiringSoon, Valid, NoEndDate, productsPending. query: nummer of relatie.
        [HttpGet]
        public async Task<IActionResult> Overview([FromQuery] string query, [FromQuery] string type, [FromQuery] string status, [FromQuery] bool includeArchived = false) =>
            Ok(await service.OverviewAsync(query, type, status, includeArchived, Aborted));

        [HttpGet("settings")]
        public async Task<IActionResult> Settings() => Ok(await service.GetSettingsAsync(Aborted));

        [HttpPut("settings")]
        [Authorize(Policy = Permissions.AdminWrites)]
        public async Task<IActionResult> UpdateSettings([FromBody] CertificateSettingsDto request) => this.ToActionResult(await service.UpdateSettingsAsync(request, Aborted));

        [HttpGet("relations/{relationId:int}")]
        public async Task<IActionResult> ForRelation(int relationId) => this.ToActionResult(await service.GetForRelationAsync(relationId, Aborted));

        // Vervangt de hele lijst, met de versie die de gebruiker zag (409 bij een gelijktijdige wijziging).
        [HttpPut("relations/{relationId:int}")]
        public async Task<IActionResult> SaveForRelation(int relationId, [FromBody] SaveCertificatesDto request) =>
            this.ToActionResult(await service.SaveForRelationAsync(relationId, request, Aborted));

        [HttpGet("own")]
        public async Task<IActionResult> Own() => Ok(await service.GetOwnAsync(Aborted));

        [HttpPut("own")]
        public async Task<IActionResult> SaveOwn([FromBody] SaveCertificatesDto request) => this.ToActionResult(await service.SaveOwnAsync(request, Aborted));

        // Een PDF of foto toevoegen: altijd een nieuwe versie (vervangen wist nooit de vorige).
        [HttpPost("{id:int}/files")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult> Upload(int id, IFormFile file)
        {
            byte[] bytes = Array.Empty<byte>();
            if (file != null)
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, Aborted);
                bytes = buffer.ToArray();
            }
            return this.ToActionResult(await service.UploadAsync(id, file?.FileName, bytes, Aborted));
        }

        [HttpGet("{id:int}/files/{fileId:int}")]
        public async Task<IActionResult> Download(int id, int fileId)
        {
            var found = await service.FileAsync(id, fileId, Aborted);
            if (found is not { } file) return NotFound(new { message = $"File {fileId} of certificate {id} does not exist." });
            Response.Headers.CacheControl = "private, no-store";
            // Inline: de browser toont de PDF of foto meteen; opslaan houdt de oorspronkelijke bestandsnaam.
            Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
            return File(file.Bytes, file.MediaType);
        }
    }
}
