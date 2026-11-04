namespace AgfAdmin.Security
{
    // Toestemmingen per gebied van het systeem. Elk gebied is een autorisatiebeleid met dezelfde naam op de endpoints van dat
    // gebied; een gebruiker heeft een gebied als zijn account de claim `agf:permission` met die waarde heeft. Nu heeft elke
    // gebruiker alle gebieden (zie DefaultUserOptions); een latere instelling per gebruiker geeft alleen minder claims mee, de
    // endpoints veranderen dan niet. Zie docs/rechten.md.
    public static class Permissions
    {
        public const string ClaimType = "agf:permission";

        public const string Purchase = "Inkoop";
        public const string Stock = "Voorraad";
        public const string Inspection = "Keuring";
        public const string Sales = "Verkoop";
        public const string MasterData = "Stamgegevens";
        public const string Admin = "Beheer";

        // Gevoelige verkoopgegevens (prijzen, marges, partijresultaat): apart te ontzeggen bovenop Verkoop. Nog op geen enkel
        // endpoint gezet, omdat er nog geen aparte endpoints of velden voor zijn; zie docs/rechten.md.
        public const string SalesFigures = "Verkoopcijfers";

        public static readonly IReadOnlyList<string> All = new[] { Purchase, Stock, Inspection, Sales, MasterData, Admin, SalesFigures };

        // Beleid voor gedeelde stamgegevens: lezen mag elke aangemelde gebruiker (artikelen, landen en relaties staan in elk
        // formulier), wijzigen alleen met Stamgegevens.
        public const string MasterDataWrites = "Stamgegevens:wijzigen";
        // Beleid voor instellingen: lezen mag iedereen (standaarden in formulieren), wijzigen alleen met Beheer.
        public const string AdminWrites = "Beheer:wijzigen";
        // Beleid voor partijen: lezen en aanmaken met Inkoop of met Voorraad (de inkoper maakt ze bij de order, de loods bij ontvangst).
        public const string PurchaseOrStock = "Inkoop|Voorraad";
    }
}
