using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag: alle regels zitten in ISupplyCommitmentService.
    [ApiController]
    [Route("api/supply-commitments")]
    public class SupplyCommitmentsController : ControllerBase
    {
        private readonly ISupplyCommitmentService _service;

        public SupplyCommitmentsController(ISupplyCommitmentService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSupplyCommitmentDto request)
        {
            var result = await _service.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string route,
            [FromQuery] string status,
            [FromQuery] int? vendorId,
            [FromQuery] int? customerId)
        {
            var filter = new SupplyCommitmentFilter { Route = route, Status = status, VendorId = vendorId, CustomerId = customerId };
            return this.ToActionResult(await _service.ListAsync(filter, HttpContext?.RequestAborted ?? default));
        }
    }
}
