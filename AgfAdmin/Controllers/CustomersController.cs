using Microsoft.AspNetCore.Mvc;
using AgfAdmin.Data;
using AgfAdmin.Models;
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
        public CustomersController(AppDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            var customers = await _context.Customers.OrderBy(c => c.Name).ToListAsync();
            return Ok(customers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] Customer customer)
        {
            customer.CreatedAt = DateTime.UtcNow;
            
            // Als er geen betaaltermijn is ingevuld, maken we er standaard 14 van
            if (customer.PaymentTermsDays <= 0) customer.PaymentTermsDays = 14;

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
            return Ok(customer);
        }

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
