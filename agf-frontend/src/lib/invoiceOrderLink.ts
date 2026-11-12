import type { InvoiceCandidate } from '../types/purchaseOrder';
import type { PackageUnit } from '../types/availability';
import { formatAmountInput, formatDecimalInput, parsePositiveDecimal } from './decimal.ts';
import { lineNet, round2 } from './invoiceMath.ts';
import { type InvoiceDraft, type LineDraft, isBlankLine, newLineKey, parseNonNegative } from './purchaseInvoiceRules.ts';

// Een inkoopfactuur gekoppeld aan de bon (stap 4b), pure functies met node:test; gespiegeld van AgfAdmin PurchaseInvoiceOrderLinkRules.
// Een regel uit de bon neemt artikel, eenheid, aantal (wat nog open is) en de prijs van de orderregel over; de gebruiker past alleen aan
// wat op de factuur anders staat. Het verschil met de bon wordt per regel getoond, en bij opslaan eenmaal per factuur gevraagd.

export interface OrderLineLink {
    lineId: number;
    orderId: number;
    orderNumber: string;
    supplierReference: string | null;
    productName: string;
    unit: string | null;
    orderUnitPrice: number | null;
    expectedQuantity: number;
    receivedQuantity: number;
    invoicedQuantity: number;
    openQuantity: number;
    priceAdjustable: boolean;
}

const PACKAGE_UNITS: readonly string[] = ['box', 'colli', 'tray', 'piece', 'dozen'];

// Waartegen gefactureerd wordt: ontvangen zodra er iets ontvangen is, anders besteld.
export const invoiceBasis = (link: Pick<OrderLineLink, 'expectedQuantity' | 'receivedQuantity'>) => (link.receivedQuantity > 0 ? link.receivedQuantity : link.expectedQuantity);

// De regels van een order als factuurregels: alleen wat nog open is; staat niets meer open (gevonden via zoeken), dan elke regel zonder
// aantal, zodat de gebruiker zelf bewust een aantal invult.
export function linesFromCandidate(order: InvoiceCandidate, defaultVat: number): LineDraft[] {
    const open = order.lines.filter((line) => line.openQuantity > 0);
    const source = open.length > 0 ? open : order.lines;
    return source.map((line) => ({
        key: newLineKey(),
        productVariantId: String(line.productVariantId),
        costArticleId: '',
        description: `${line.productName} · ${line.productVariantName}`.trim(),
        quantity: open.length > 0 ? formatDecimalInput(line.openQuantity, 3) : '',
        unit: (line.unit !== null && PACKAGE_UNITS.includes(line.unit) ? line.unit : '') as PackageUnit | '',
        unitPrice: line.unitPrice === null ? '' : formatAmountInput(line.unitPrice),
        discount: '0',
        vat: formatDecimalInput(defaultVat, 2),
        orderLine: {
            lineId: line.id, orderId: order.id, orderNumber: order.orderNumber, supplierReference: order.supplierReference,
            productName: `${line.productName} ${line.productVariantName}`.trim(), unit: line.unit, orderUnitPrice: line.unitPrice,
            expectedQuantity: line.expectedQuantity, receivedQuantity: line.receivedQuantity, invoicedQuantity: line.invoicedQuantity, openQuantity: line.openQuantity,
            priceAdjustable: order.priceAdjustable,
        },
    }));
}

// Orderregels toevoegen: een leeg begin (alleen lege regels) wordt vervangen; een orderregel die er al staat komt er niet nog eens bij.
export function addOrderLines(draft: InvoiceDraft, order: InvoiceCandidate, defaultVat: number): InvoiceDraft {
    const present = new Set(draft.lines.map((line) => line.orderLine?.lineId).filter((id) => id !== undefined));
    const added = linesFromCandidate(order, defaultVat).filter((line) => !present.has(line.orderLine!.lineId));
    const kept = draft.lines.every(isBlankLine) ? [] : draft.lines;
    return { ...draft, lines: [...kept, ...added] };
}

export interface LineComparison {
    quantity: number | null;
    orderAmount: number | null;
    invoiceAmount: number | null;
    invoiceUnitPrice: number | null;
    perUnit: number | null;   // factuur min bon per eenheid; null zonder prijs op de bon of zonder geldige factuurprijs
    total: number | null;     // factuurbedrag min bonbedrag voor hetzelfde aantal
    differs: boolean;         // geen prijs op de bon, of een andere prijs per eenheid
    over: boolean;            // samen met wat al gefactureerd is meer dan besteld of ontvangen
    partial: boolean;         // minder dan nog open
}

// Zoals het formulier: leeg is 0, nul mag (een gratis regel), negatief niet.
const parse = (input: string, maxDecimals: number) => {
    const result = parseNonNegative(input.trim() === '' ? '0' : input, maxDecimals);
    return result.ok ? result.value : null;
};

export function compareLine(line: LineDraft): LineComparison | null {
    const link = line.orderLine;
    if (!link) return null;
    const quantityResult = parsePositiveDecimal(line.quantity);
    const quantity = quantityResult.ok ? quantityResult.value : null;
    const price = line.unitPrice.trim() === '' ? null : parse(line.unitPrice, 2);
    const discount = parse(line.discount, 2);
    const invoiceUnitPrice = price === null || discount === null ? null : round2(price * (1 - discount / 100));
    const invoiceAmount = quantity === null || price === null || discount === null ? null : lineNet(quantity, price, discount);
    const orderAmount = quantity === null || link.orderUnitPrice === null ? null : round2(quantity * link.orderUnitPrice);
    return {
        quantity, orderAmount, invoiceAmount, invoiceUnitPrice,
        perUnit: invoiceUnitPrice === null || link.orderUnitPrice === null ? null : round2(invoiceUnitPrice - link.orderUnitPrice),
        total: invoiceAmount === null || orderAmount === null ? null : round2(invoiceAmount - orderAmount),
        differs: invoiceUnitPrice !== null && link.orderUnitPrice !== invoiceUnitPrice,
        over: quantity !== null && link.invoicedQuantity + quantity > invoiceBasis(link) + 1e-9,
        partial: quantity !== null && quantity < link.openQuantity,
    };
}

export interface LinkCheck {
    differing: LineDraft[];
    over: LineDraft[];
    // Alle orders met een verschil kunnen de factuurprijs krijgen (open magazijnorder); anders alleen "verschil vastleggen".
    adjustable: boolean;
    totalDifference: number;
    needsQuestion: boolean;
}

export function linkCheck(draft: InvoiceDraft): LinkCheck {
    const linked = draft.lines.filter((line) => line.orderLine !== undefined && !isBlankLine(line));
    const compared = linked.map((line) => ({ line, comparison: compareLine(line)! }));
    const differing = compared.filter((c) => c.comparison.differs).map((c) => c.line);
    const over = compared.filter((c) => c.comparison.over).map((c) => c.line);
    return {
        differing, over,
        adjustable: differing.every((line) => line.orderLine!.priceAdjustable),
        totalDifference: round2(compared.reduce((sum, c) => sum + (c.comparison.differs ? c.comparison.total ?? 0 : 0), 0)),
        needsQuestion: differing.length > 0 || over.length > 0,
    };
}

// De bon zoals de gebruiker hem kent: het ordernummer van de leverancier met het eigen IO-nummer erbij.
export const orderLabel = (link: Pick<OrderLineLink, 'orderNumber' | 'supplierReference'>) =>
    link.supplierReference ? `${link.supplierReference} (${link.orderNumber})` : link.orderNumber;
