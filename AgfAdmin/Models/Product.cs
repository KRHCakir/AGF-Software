using System.Text.Json.Serialization;

namespace AgfAdmin.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } // e.g. "Tomato"
        public string? ImageUrl { get; set; }

        public ICollection<ProductVariant> Variants { get; set; }
    }
}