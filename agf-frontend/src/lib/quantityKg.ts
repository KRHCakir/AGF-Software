// Rekenregel onder een aantal: "1.560 colli × 10 kg = 15.600,0 kg netto · 16.068,0 kg bruto", met de standaarden van het artikel.
// Alleen weergave: er wordt niets van opgeslagen en het verandert nooit opgegeven of gewogen kilo's. Zonder netto kg per
// verpakking (gewogen artikel) geen rekenregel. Geen imports, zodat het met node:test te toetsen is.

// Zelfde veldnamen als de variant (VariantOption) en de aanvoerregel (SupplyCommitmentLine), die dus direct passen.
export interface PackageKgDefaults {
    defaultNetKgPerPackage?: number | null;
    defaultGrossKgPerPackage?: number | null;
}

// 3 decimalen, half omhoog (zoals palletTotal en WeightRules.CalculatedTotal); de weergave rondt daarna af op 1 decimaal.
export const times = (quantity: number, kg: number) => Math.round(quantity * kg * 1000 + Number.EPSILON) / 1000;

export function packageKg(quantity: number | null, defaults: PackageKgDefaults | undefined): { netKgPerPackage: number; netKg: number; grossKg: number | null } | null {
    const net = defaults?.defaultNetKgPerPackage ?? null;
    if (quantity === null || !(quantity > 0) || net === null || !(net > 0)) return null;
    const gross = defaults?.defaultGrossKgPerPackage ?? null;
    return { netKgPerPackage: net, netKg: times(quantity, net), grossKg: gross !== null && gross > 0 ? times(quantity, gross) : null };
}

// Kg in de rekenregel altijd met precies 1 decimaal (15.600,0 kg), in de notatie van de schermtaal.
export function formatKgOneDecimal(value: number, locale: string): string {
    return `${new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value)} kg`;
}
