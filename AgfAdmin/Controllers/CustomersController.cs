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
        public CustomersController(AppDbContext context, ICustomerService customers) { _context = context; _customers = customers; }

        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            var customers = await _context.Customers.OrderBy(c => c.Name).ToListAsync();
            return Ok(customers);
        }

        // Klant aanmaken vanuit Verkoop: gevalideerd en idempotent (requestId). 201 nieuw, 200 identieke herhaling,
        // 400 validatie, 409 gelijkende naam zonder bevestiging of afwijkende herhaling. GET en PUT zijn ongewijzigd.
        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateOperationalCustomerDto request) =>
            this.ToActionResult(await _customers.CreateAsync(request, HttpContext?.RequestAborted ?? default));

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] Customer updatedCustomer)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            customer.Name = updatedCustomer.Name;
            customer.BillingAddress = updatedCustomer.BillingAddress;
            customer.VATNumber = updatedCustomer.VATNumber;
            customer.PaymentTermsDays = updatedCustomer.PaymentTermsDays;
            customer.Email = updatedCustomer.Email;
            customer.Phone = updatedCustomer.Phone;

            await _context.SaveChangesAsync();
            return Ok(customer);
        }
    }
}
