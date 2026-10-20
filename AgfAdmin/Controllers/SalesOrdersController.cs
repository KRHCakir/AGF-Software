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
        private readonly ISalesDispatchService _dispatches;
        private readonly ISalesDispatchCorrectionService _corrections;
        private readonly IDirectTradeAllocationService _allocations;

        public SalesOrdersController(ISalesOrderService orders, IStockReservationService reservations, ISalesDispatchService dispatches, ISalesDispatchCorrectionService corrections, IDirectTradeAllocationService allocations)
        {
            _orders = orders;
            _reservations = reservations;
            _dispatches = dispatches;
            _corrections = corrections;
            _allocations = allocations;
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

        // Annuleren: verplichte reden en verwachte orderversie; alle Active reserveringen worden in dezelfde transactie vrijgegeven.
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelSalesOrderDto request) =>
            this.ToActionResult(await _orders.CancelAsync(id, request, HttpContext?.RequestAborted ?? default));

        // Alleen lezen: orderhistorie (append-only).
        [HttpGet("{id:int}/audit-events")]
        public async Task<IActionResult> GetAuditEvents(int id) =>
            this.ToActionResult(await _orders.GetAuditEventsAsync(id, HttpContext?.RequestAborted ?? default));

        // Verzenden: gekozen Active reserveringen in hun geheel verbruiken; één transactie met mutaties, audit en versies.
        [HttpPost("{id:int}/dispatches")]
        public async Task<IActionResult> Dispatch(int id, [FromBody] CreateSalesDispatchDto request)
        {
            var result = await _dispatches.DispatchAsync(id, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id }, created));
        }

        // Administratieve correctie van een hele verzending (goederen hebben het magazijn niet verlaten, door de gebruiker bevestigd).
        [HttpPost("{id:int}/dispatches/{dispatchId:int}/corrections")]
        public async Task<IActionResult> CorrectDispatch(int id, int dispatchId, [FromBody] CorrectSalesDispatchDto request)
        {
            var result = await _corrections.CorrectAsync(id, dispatchId, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id }, created));
        }

        [HttpGet("{id:int}/dispatches")]
        public async Task<IActionResult> GetDispatches(int id) =>
            this.ToActionResult(await _dispatches.ListAsync(id, HttpContext?.RequestAborted ?? default));

        // Klantgebonden DirectTrade-aanvoer (zelfde klant, variant en eenheid) die aan deze regel kan worden toegewezen; verwachte aanvoer, geen voorraad.
        [HttpGet("{id:int}/lines/{lineId:int}/direct-trade-supply")]
        public async Task<IActionResult> GetDirectTradeSupply(int id, int lineId) =>
            this.ToActionResult(await _allocations.GetAllocatableSupplyAsync(id, lineId, HttpContext?.RequestAborted ?? default));

        // Toewijzen van verwachte, klantgebonden aanvoer: alleen hoeveelheid en eenheid, met expliciete bevestiging; geen voorraadmutatie.
        [HttpPost("{id:int}/lines/{lineId:int}/direct-trade-allocations")]
        public async Task<IActionResult> AllocateDirectTrade(int id, int lineId, [FromBody] CreateDirectTradeAllocationDto request)
        {
            var result = await _allocations.AllocateAsync(id, lineId, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id }, created));
        }

        [HttpPost("{id:int}/lines/{lineId:int}/reservations")]
        public async Task<IActionResult> Reserve(int id, int lineId, [FromBody] CreateStockReservationDto request)
        {
            var result = await _reservations.ReserveAsync(id, lineId, request, HttpContext?.RequestAborted ?? default);
            return this.ToActionResult(result, created => CreatedAtAction(nameof(GetById), new { id }, created));
        }
    }
}
