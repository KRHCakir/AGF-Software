// Oude adressen blijven werken: /supply (met #plan, #receive of zonder hash) verwijst door naar het scherm met de nieuwe naam.
// Klantgebonden aanvoer had geen eigen adres; die zat achter de keuze "Directe handel" op /supply. Geen imports: met node:test te toetsen.
export function supplyRedirect(hash: string): string {
    const value = hash.replace(/^#/, '').toLowerCase();
    if (value === 'receive') return '/receipts';
    if (value === 'direct' || value === 'directtrade') return '/direct-trade';
    return '/purchase-orders/new';
}
