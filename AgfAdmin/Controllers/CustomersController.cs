using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class CustomersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICustomerService _customers;
        private readonly RelationService _relations;
        public CustomersController(AppDbContext context, ICustomerService customers, RelationService relations) { _context = context; _customers = customers; _relations = relations; }

        // Klantenlijst voor pickers: dezelfde velden als voorheen plus zoekcode en actief-vlag van de relatie. Inactieve klanten
        // blijven in de lijst (bestaande concepten en historie), de schermen tonen ze apart en de server weigert nieuwe documenten.
        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            var customers = await _context.Customers.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id, c.RelationId, c.Name, c.VATNumber, c.BillingAddress, c.ShippingAddress, c.ContactPerson, c.Email, c.Phone, c.PaymentTermsDays, c.CreatedAt, c.RequestId,
                    SearchCode = c.Relation.SearchCode, IsActive = c.Relation.IsActive
                })
                .ToListAsync();
            return Ok(customers);
        }

        // Openstaand krediet (EUR) tegenover kredietlimiet en verzekering; alleen signalering. 404 als de klant niet bestaat.
        [HttpGet("{id:int}/credit")]
        public async Task<IActionResult> GetCredit(int id)
        {
            var exposure = await new CreditExposureService(_context).GetExposureAsync(id, _context.BusinessCalendar.Today(), HttpContext?.RequestAborted ?? default);
            return exposure == null ? NotFound(new { message = $"Customer {id} does not exist." }) : Ok(exposure);
        }

        // Klant aanmaken vanuit Verkoop: gevalideerd en idempotent (requestId). 201 nieuw, 200 identieke herhaling,
        // 400 validatie, 409 gelijkende naam zonder bevestiging of afwijkende herhaling. GET en PUT zijn ongewijzigd.
        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateOperationalCustomerDto request) =>
            this.ToActionResult(await _customers.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        // Legacy route (zelfde velden als voorheen): de wijziging gaat naar de relatie en wordt teruggespiegeld op de klantrol.
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] Customer updatedCustomer)
        {
            if (updatedCustomer == null || string.IsNullOrWhiteSpace(updatedCustomer.Name)) return BadRequest("Customer name is required");
            var current = await _context.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
            if (current == null) return NotFound();
            var customer = await _relations.UpdateLegacyCustomerAsync(id, new CreateCustomerDto
            {
                Name = updatedCustomer.Name,
                VATNumber = updatedCustomer.VATNumber,
                BillingAddress = updatedCustomer.BillingAddress,
                ShippingAddress = current.ShippingAddress,
                ContactPerson = current.ContactPerson,
                Email = updatedCustomer.Email,
                Phone = updatedCustomer.Phone,
                PaymentTermsDays = updatedCustomer.PaymentTermsDays
            }, HttpContext?.RequestAborted ?? default);
            return customer == null ? NotFound() : Ok(customer);
        }
    }
}
