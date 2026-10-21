namespace AgfAdmin.DTOs
{
    // PUT api/products/variants/{id}/packaging-settings: standaard verpakkingen per pallet (alleen invoerhulp).
    public sealed class UpdatePackagingSettingsDto
    {
        public decimal? DefaultPackagesPerPallet { get; set; }
    }
}
