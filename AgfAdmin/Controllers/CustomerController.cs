// CustomerController.cs
using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class CustomerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly RelationService _relations;
        public CustomerController(AppDbContext context, RelationService relations)
        {
            _context = context;
            _relations = relations;
        }

        // Legacy route: de platte velden gaan sinds de relatiewerkplek via RelationService (relatie + klantrol), nooit rechtstreeks.
        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Customer name is required");

            var customer = await _relations.CreateLegacyCustomerAsync(dto, HttpContext?.RequestAborted ?? default);

            return Ok(new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                VATNumber = customer.VATNumber,
                BillingAddress = customer.BillingAddress,
                ShippingAddress = customer.ShippingAddress,
                ContactPerson = customer.ContactPerson,
                Email = customer.Email,
                Phone = customer.Phone,
                PaymentTermsDays = customer.PaymentTermsDays,
                CreatedAt = customer.CreatedAt
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            var customers = await _context.Customers
                .Select(c => new CustomerDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    VATNumber = c.VATNumber,
                    BillingAddress = c.BillingAddress,
                    ShippingAddress = c.ShippingAddress,
                    ContactPerson = c.ContactPerson,
                    Email = c.Email,
                    Phone = c.Phone,
                    PaymentTermsDays = c.PaymentTermsDays,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return Ok(customers);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCustomer(int id)
        {
            var customer = await _context.Customers
                .Where(c => c.Id == id)
                .Select(c => new CustomerDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    VATNumber = c.VATNumber,
                    BillingAddress = c.BillingAddress,
                    ShippingAddress = c.ShippingAddress,
                    ContactPerson = c.ContactPerson,
                    Email = c.Email,
                    Phone = c.Phone,
                    PaymentTermsDays = c.PaymentTermsDays,
                    CreatedAt = c.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (customer == null)
                return NotFound();

            return Ok(customer);
        }

        // Legacy route: wijzigingen gaan naar de relatie (enige bron) en worden teruggespiegeld.
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CreateCustomerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Customer name is required");
            var customer = await _relations.UpdateLegacyCustomerAsync(id, dto, HttpContext?.RequestAborted ?? default);
            if (customer == null) return NotFound();

            return Ok(new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                VATNumber = customer.VATNumber,
                BillingAddress = customer.BillingAddress,
                ShippingAddress = customer.ShippingAddress,
                ContactPerson = customer.ContactPerson,
                Email = customer.Email,
                Phone = customer.Phone,
                PaymentTermsDays = customer.PaymentTermsDays,
                CreatedAt = customer.CreatedAt
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
                return NotFound();

            // Inactiveren in plaats van verwijderen zodra documenten naar de klantrol verwijzen; historie blijft leesbaar.
            var referenced = await _context.Orders.AnyAsync(o => o.CustomerId == id)
                || await _context.SalesOrders.AnyAsync(o => o.CustomerId == id)
                || await _context.SupplyCommitments.AnyAsync(c => c.CustomerId == id)
                || await _context.Invoices.AnyAsync(i => i.CustomerId == id);
            if (referenced)
                return Conflict(new { message = "Cannot delete a customer that documents refer to; mark the relation inactive instead." });

            var relation = await _context.Relations.Include(r => r.Vendor).SingleAsync(r => r.Id == customer.RelationId);
            _context.Customers.Remove(customer);
            if (relation.Vendor == null)
            {
                _context.Relations.Remove(relation); // zonder andere rol blijft er geen onderneming over
            }
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}