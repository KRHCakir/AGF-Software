// Eén geldberekening voor inkoopfacturen, gespiegeld in AgfAdmin/Services/InvoiceMath.cs: per regel afronden op
// 2 decimalen (half omhoog) en daarna optellen, zodat scherm, server en opgeslagen totalen nooit verschillen.
// Pure module zonder React of i18n, zodat ze met node:test te toetsen is.

export function round2(value: number): number {
    const scaled = value * 100;
    return Math.round(scaled + Number.EPSILON * Math.abs(scaled)) / 100;
}

// Regelbedrag exclusief btw: aantal × prijs per eenheid, minus korting in procenten.
export function lineNet(quantity: number, unitPrice: number, discountPercentage: number): number {
    return round2(quantity * unitPrice * (1 - discountPercentage / 100));
}

export function lineVat(net: number, vatPercentage: number): number {
    return round2(net * vatPercentage / 100);
}

export interface InvoiceTotals {
    net: number;
    vat: number;
    gross: number;
}

// Documenttotalen: som van de afgeronde regelbedragen (centen opgeteld als gehele getallen, dus zonder zwevende-kommaruis).
export function totals(lines: readonly { net: number; vat: number }[]): InvoiceTotals {
    let netCents = 0;
    let vatCents = 0;
    for (const line of lines) {
        netCents += Math.round(line.net * 100);
        vatCents += Math.round(line.vat * 100);
    }
    return { net: netCents / 100, vat: vatCents / 100, gross: (netCents + vatCents) / 100 };
}
