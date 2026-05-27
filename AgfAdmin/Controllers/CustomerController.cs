// CustomerController.cs
using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.DTOs;
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
        public CustomerController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Customer name is required");

            var customer = new Customer
            {
                Name = dto.Name.Trim(),
                VATNumber = dto.VATNumber,
                BillingAddress = dto.BillingAddress,
                ShippingAddress = dto.ShippingAddress,
                ContactPerson = dto.ContactPerson,
                Email = dto.Email,
                Phone = dto.Phone,
                PaymentTermsDays = dto.PaymentTermsDays
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

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

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CreateCustomerDto dto)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            customer.Name = dto.Name.Trim();
            customer.VATNumber = dto.VATNumber;
            customer.BillingAddress = dto.BillingAddress;
            customer.ShippingAddress = dto.ShippingAddress;
            customer.ContactPerson = dto.ContactPerson;
            customer.Email = dto.Email;
            customer.Phone = dto.Phone;
            customer.PaymentTermsDays = dto.PaymentTermsDays;

            await _context.SaveChangesAsync();

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

            // Check if customer has orders
            var hasOrders = await _context.Orders.AnyAsync(o => o.CustomerId == id);
            if (hasOrders)
                return BadRequest("Cannot delete customer with existing orders");

            _context.Customers.Remove(customer);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}