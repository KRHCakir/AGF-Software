using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: landenlijst en eigen aliassen (CountryService). De landen zelf zijn vast en worden hier nooit gewijzigd.
    [ApiController]
    [Route("api/countries")]
    public class CountriesController : ControllerBase
    {
        private readonly CountryService _service;

        public CountriesController(CountryService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Reference() => Ok(await _service.ReferenceAsync(HttpContext?.RequestAborted ?? default));

        [HttpPost("aliases")]
        public async Task<IActionResult> AddAlias([FromBody] AddCountryAliasDto request) =>
            this.ToActionResult(await _service.AddAliasAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpDelete("aliases/{id:int}")]
        public async Task<IActionResult> RemoveAlias(int id) =>
            this.ToActionResult(await _service.RemoveAliasAsync(id, HttpContext?.RequestAborted ?? default));
    }
}
