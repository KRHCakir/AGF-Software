using AgfAdmin.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AgfAdmin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult> GetDashboardData()
        {
            var today = DateTime.UtcNow.Date;

            // 1. Total Sales Today
            // Aggregate totals from active Invoices created today
            decimal totalSalesToday = await _context.Invoices
                .Where(i => i.InvoiceDate >= today)
                .SumAsync(i => i.TotalAmount);

            // 2. Unpaid Invoices
            // Count incoming invoices not marked as paid
            int unpaidInvoices = await _context.IncomingInvoices
                .Where(i => i.Status != "Paid" && i.Status != "Approved")
                .CountAsync();

            // 3. Shipments Arriving
            var tomorrow = DateTime.UtcNow.AddDays(1);
            int shipmentsArriving = await _context.Shipments
                .Where(s => s.EstimatedTimeOfArrival <= tomorrow && s.EstimatedTimeOfArrival >= DateTime.UtcNow)
                .CountAsync();

            // 4. Margin %
            string margin = "18.2%";

            // 5. Recent Orders (Top 5)
            var recentOrders = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.Lines)
                    .ThenInclude(l => l.ProductVariant)
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new {
                    id = o.Id,
                    orderNumber = "ORD-" + o.Id, // Mock format
                    customerName = o.CustomCustomerName ?? (o.Customer != null ? o.Customer.Name : "Walk-in"),
                    productName = o.Lines.Count == 1 ? o.Lines.FirstOrDefault().ProductVariant.Name : $"{o.Lines.Count} items",
                    amount = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
                    status = o.Status
                })
                .ToListAsync();

            // 6. In Transit Container Status
            var activeShipments = await _context.Shipments
                .OrderBy(s => s.EstimatedTimeOfArrival)
                .Take(5)
                .ToListAsync();

            return Ok(new
            {
                kpis = new
                {
                    totalSalesToday = totalSalesToday,
                    unpaidInvoices = unpaidInvoices,
                    shipmentsArriving = shipmentsArriving,
                    margin = margin
                },
                recentOrders = recentOrders,
                shipments = activeShipments
            });
        }
    }
}
