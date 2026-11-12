namespace AgfAdmin.DTOs
{
    // Transport op een verkooporder (documenten deel 2). Leeg laten = de standaard (laadplaats: de loods; losplaats: het afleveradres van
    // de klant; losreferentie: de klantreferentie; soort pallet: de standaard van het bedrijf). Laden op is dezelfde als op de laadbon.
    public sealed class SaveSalesOrderTransportDto
    {
        public int? ExpectedVersion { get; set; }        // verplicht als er al transport is
        public string Mode { get; set; }                 // Carrier of Own
        public int? CarrierRelationId { get; set; }
        public string LoadingPlace { get; set; }
        public int? UnloadingAddressId { get; set; }
        public string UnloadingPlace { get; set; }
        // Laden op staat op de order (ook op de laadbon): leeg laat een al ingevulde laadtijd staan; wissen gaat expliciet met ClearLoadAt.
        public DateTime? LoadAt { get; set; }
        public bool ClearLoadAt { get; set; }
        public DateTime? UnloadDate { get; set; }
        public decimal? TemperatureC { get; set; }
        public string LoadingReference { get; set; }
        public string UnloadingReference { get; set; }
        public string Remarks { get; set; }
        public string PalletKind { get; set; }
    }

    public sealed class SalesOrderTransportDto
    {
        public int SalesOrderId { get; set; }
        public bool Exists { get; set; }
        public string Mode { get; set; }
        public int? CarrierRelationId { get; set; }
        public string CarrierName { get; set; }
        public string CarrierAddress { get; set; }
        public string LoadingPlace { get; set; }
        public string DefaultLoadingPlace { get; set; }
        public int? UnloadingAddressId { get; set; }
        public string UnloadingPlace { get; set; }
        public string DefaultUnloadingPlace { get; set; }
        public List<TransportAddressOptionDto> UnloadingAddresses { get; set; } = new();
        public DateTime? LoadAt { get; set; }
        public DateTime? UnloadDate { get; set; }
        public decimal? TemperatureC { get; set; }
        public string LoadingReference { get; set; }
        public string UnloadingReference { get; set; }
        public string DefaultUnloadingReference { get; set; }
        public string Remarks { get; set; }
        public string PalletKind { get; set; }
        public string DefaultPalletKind { get; set; }
        public int Version { get; set; }
        // Er is al een transportopdracht: dan kan het transport niet meer weg, alleen veranderen (nieuwe versie).
        public bool HasTransportOrder { get; set; }
    }

    // Een adres van de klant om te lossen: het afleveradres of het hoofdadres, als één regel.
    public sealed class TransportAddressOptionDto
    {
        public int Id { get; set; }
        public string Purpose { get; set; }
        public string Text { get; set; }
        public bool IsDefault { get; set; }
    }

    // Instellingen van de transportopdracht per bedrijf: taal van de kopteksten (en, nl) en de standaard soort pallet.
    public sealed class TransportOrderSettingsDto
    {
        public string Language { get; set; }
        public string PalletKind { get; set; }
    }
}
