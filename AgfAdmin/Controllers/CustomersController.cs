using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgfAdmin.Data;

namespace AgfAdmin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomersController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CustomersController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{id}/standing")]
        public async Task<IActionResult> GetCustomerStanding(int id)
        {
            var overdueCount = await _context.Invoices
                .Where(i => i.CustomerId == id && i.PaymentStatus == "Overdue")
                .CountAsync();
                
            var pendingCount = await _context.Invoices
                .Where(i => i.CustomerId == id && i.PaymentStatus == "Pending")
                .CountAsync();

            return Ok(new {
                CustomerId = id,
                OverdueInvoicesCount = overdueCount,
                PendingInvoicesCount = pendingCount,
                IsHighRisk = overdueCount >= 3
            });
        }
    }
}
