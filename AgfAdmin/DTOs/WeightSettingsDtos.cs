namespace AgfAdmin.DTOs
{
    // PUT api/products/variants/{id}/weight-settings: gewichtsmethode en standaard kg per verpakking van een variant.
    public sealed class UpdateWeightSettingsDto
    {
        public string WeightMethod { get; set; }
        public decimal? DefaultNetKgPerPackage { get; set; }
    }
}
