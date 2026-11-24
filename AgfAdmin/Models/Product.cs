using System.Text.Json.Serialization;

namespace AgfAdmin.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } // e.g. "Tomato"
        public string? ImageUrl { get; set; }
        // Feedback 22 november: de kleurwaarden voor partijen van dit product (bijvoorbeeld groen en geel, of 1 tot 6 voor ananas), één per
        // regel in de opgeslagen volgorde. Leeg = geen kleur voor dit product. Zie LotColorRules.
        public string? ColorValues { get; set; }

        public ICollection<ProductVariant> Variants { get; set; }
    }
}