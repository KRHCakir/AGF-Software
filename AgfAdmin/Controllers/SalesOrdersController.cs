using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Controllers
{
    // Dunne laag voor de schone verkoopflow. Raakt nooit de legacy /api/order-endpoints.
    [ApiController]
    [Route("api/sales-orders")]
    public class SalesOrdersController : ControllerBase
    {
        private readonly ISalesOrderService _orders;
        private readonly IStockReservationService _reservations;

        public SalesOrdersController(ISalesOrderService orders, IStockReservationService reservations)
        {
            _orders = orders;
            _reservations = reservations;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSalesOrderDto request)
        {
            var result = await _orders.CreateAsync(request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id = created.Id }, created));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id) =>
            this.ToActionResult(await _orders.GetAsync(id, HttpContext?.RequestAborted ?? default));

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? customerId, [FromQuery] string status) =>
            this.ToActionResult(await _orders.ListAsync(new SalesOrderFilter { CustomerId = customerId, Status = status }, HttpContext?.RequestAborted ?? default));

        [HttpGet("{id:int}/lines/{lineId:int}/reservable-stock")]
        public async Task<IActionResult> GetReservableStock(int id, int lineId) =>
            this.ToActionResult(await _orders.GetReservableStockAsync(id, lineId, HttpContext?.RequestAborted ?? default));

        [HttpPost("{id:int}/lines/{lineId:int}/reservations")]
        public async Task<IActionResult> Reserve(int id, int lineId, [FromBody] CreateStockReservationDto request)
        {
            var result = await _reservations.ReserveAsync(id, lineId, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id }, created));
        }
    }
}
