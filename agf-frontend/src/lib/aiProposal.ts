import type { AiExtraction } from '../types/aiReading';
import type { PackageUnit } from '../types/availability';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../types/purchaseInvoice';
import { formatAmountInput, formatDecimalInput } from './decimal.ts';
import { type InvoiceDraft, changeCurrency, changeInvoiceDate, emptyDraft, newLineKey, selectVendor } from './purchaseInvoiceRules.ts';

// Het AI-voorstel als concept van het factuurformulier (pure functies, met node:test). Wat de AI niet vond blijft leeg; niets wordt
// verzonnen. De leverancier alleen als die eenduidig gevonden is; een kostenartikel nooit automatisch (alleen als voorstel bij de regel).

const PACKAGE_UNITS: readonly string[] = ['box', 'colli', 'tray', 'piece', 'dozen'];

export function draftFromProposal(extraction: AiExtraction, settings: PurchaseInvoiceSettings, vendors: readonly PurchaseVendor[], today: string): InvoiceDraft {
    const proposal = extraction.proposal;
    let draft = emptyDraft(today, settings);
    if (proposal === null) return draft;
    // Geen gevonden factuurdatum: leeg laten (dus ook de vervaldatum), zodat opslaan erom vraagt; niet stilzwijgend vandaag.
    draft = { ...draft, header: { ...draft.header, invoiceDate: '', dueDate: '' } };
    const vendor = vendors.find((candidate) => candidate.id === extraction.matches?.suggestedVendorId && candidate.isActive) ?? null;
    if (vendor !== null) draft = selectVendor(draft, vendor, settings);
    draft = { ...draft, header: { ...draft.header, invoiceNumber: proposal.invoiceNumber ?? '' } };
    if (proposal.invoiceDate !== null) draft = changeInvoiceDate(draft, proposal.invoiceDate);
    if (proposal.currency !== null && proposal.currency !== draft.header.currency) draft = changeCurrency(draft, proposal.currency);
    const lines = proposal.lines.map((line) => ({
        key: newLineKey(),
        productVariantId: '',
        costArticleId: '',
        description: line.description ?? '',
        quantity: line.quantity === null ? '' : formatDecimalInput(line.quantity, 3),
        unit: (line.unit !== null && PACKAGE_UNITS.includes(line.unit) ? line.unit : '') as PackageUnit | '',
        // Een prijs met meer dan twee decimalen blijft zichtbaar zoals gelezen; opslaan vraagt dan om afronden.
        unitPrice: line.unitPrice === null ? '' : Number.isInteger(Math.round(line.unitPrice * 10000) / 100) ? formatAmountInput(line.unitPrice) : formatDecimalInput(line.unitPrice, 4),
        discount: '0',
        vat: line.vatPercentage === null ? '' :formatDecimalInput(line.vatPercentage, 2),
    }));
    return { ...draft, lines: lines.length === 0 ? draft.lines : lines };
}

// De controles per veld van het formulier: de id van het invoerveld (zoals fieldProps die maakt) en de codes. Regelvelden heten
// lines[i].veld; de omschrijving staat in het artikelveld (lines[i].item).
export function problemsByField(extraction: AiExtraction | null): Record<string, string[]> {
    const byField: Record<string, string[]> = {};
    for (const problem of extraction?.problems ?? []) {
        const field = problem.field === 'vendor' ? 'vendorId' : problem.field.replace(/\.description$/, '.item');
        const codes = byField[field] ?? (byField[field] = []);
        if (!codes.includes(problem.code)) codes.push(problem.code);
    }
    return byField;
}

// Klopt het voorstel nog niet helemaal (een controle die het vangnet liet lezen staat nog open)? Dan een gele waarschuwing bovenaan.
const SERIOUS = new Set(['linesMismatch', 'vatMismatch', 'grossMismatch', 'invalidDate']);
export function needsAttention(extraction: AiExtraction): boolean {
    return extraction.problems.some((problem) => SERIOUS.has(problem.code) || (problem.code === 'missing' && ['vendor', 'invoiceNumber', 'invoiceDate', 'totalNet', 'totalGross', 'lines'].includes(problem.field)));
}
