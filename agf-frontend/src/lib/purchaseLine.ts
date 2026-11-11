// Rekenregels van de inkooporderregel (feedback 11 november), spiegel van PurchaseLineMath in de backend; de server is leidend.
// Regeltotaal = aantal × prijs per verpakking op centen; pallets = colli / colli per pallet (de eigen palletinvoer van een oudere regel,
// anders het artikel), niet afgerond; het totaal is de som, daarna op twee decimalen, nooit naar hele pallets (2,08 blijft 2,08).
// Pure module zonder React (node:test); imports met .ts-extensie.

import { round2 } from './invoiceMath.ts';

export function lineTotal(quantity: number | null, unitPrice: number | null): number | null {
    return quantity === null || unitPrice === null ? null : round2(quantity * unitPrice);
}

export function packagesPerPallet(lineValue: number | null | undefined, articleDefault: number | null | undefined): number | null {
    if (lineValue != null && lineValue > 0) return lineValue;
    return articleDefault != null && articleDefault > 0 ? articleDefault : null;
}

export function pallets(quantity: number | null, perPallet: number | null): number | null {
    return quantity === null || perPallet === null || perPallet <= 0 ? null : quantity / perPallet;
}

// Pallets altijd op twee decimalen (0,40; 2,08), in de notatie van de schermtaal.
export function formatPallets(value: number, locale: string): string {
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

export interface PurchaseLineAmounts {
    quantity: number | null;
    netKg: number | null;
    grossKg: number | null;
    total: number | null;
    pallets: number | null;
}

export interface PurchaseLineTotals {
    quantity: number;
    netKg: number | null;
    grossKg: number | null;
    amount: number | null;
    pallets: number | null;
    linesWithoutNet: number;
    linesWithoutGross: number;
    linesWithoutPrice: number;
    linesWithoutPallets: number;
}

// Totalen onder de regels; kg, bedrag en pallets alleen van de regels die ze hebben (de rest wordt apart geteld).
export function sumLines(lines: readonly PurchaseLineAmounts[]): PurchaseLineTotals {
    const sum = (pick: (line: PurchaseLineAmounts) => number | null, digits: number) => {
        const values = lines.map(pick).filter((value): value is number => value !== null);
        if (values.length === 0) return null;
        const factor = 10 ** digits;
        return Math.round(values.reduce((total, value) => total + value, 0) * factor) / factor;
    };
    const exactPallets = sum((line) => line.pallets, 6);
    return {
        quantity: sum((line) => line.quantity, 3) ?? 0,
        netKg: sum((line) => line.netKg, 3),
        grossKg: sum((line) => line.grossKg, 3),
        amount: sum((line) => line.total, 2),
        pallets: exactPallets === null ? null : round2(exactPallets),
        linesWithoutNet: lines.filter((line) => line.netKg === null).length,
        linesWithoutGross: lines.filter((line) => line.grossKg === null).length,
        linesWithoutPrice: lines.filter((line) => line.total === null).length,
        linesWithoutPallets: lines.filter((line) => line.pallets === null).length,
    };
}
