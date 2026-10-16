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
            return result.Status == ServiceResultStatus.Created
                ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
                : ToActionResult(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            ToActionResult(await _service.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string route,
            [FromQuery] string status,
            [FromQuery] int? vendorId,
            [FromQuery] int? customerId)
        {
            var filter = new SupplyCommitmentFilter { Route = route, Status = status, VendorId = vendorId, CustomerId = customerId };
            return ToActionResult(await _service.ListAsync(filter, HttpContext?.RequestAborted ?? default));
        }

        private IActionResult ToActionResult<T>(ServiceResult<T> result) => result.Status switch
        {
            ServiceResultStatus.Ok => Ok(result.Value),
            ServiceResultStatus.Created => Ok(result.Value),
            ServiceResultStatus.Validation => BadRequest(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Key, e => e.Value))),
            ServiceResultStatus.NotFound => NotFound(new { message = result.Message }),
            ServiceResultStatus.Conflict => Conflict(new { message = result.Message }),
            _ => throw new InvalidOperationException($"Unhandled service result status {result.Status}.")
        };
    }
}
