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
    // Valuta-ronde: valuta en koers van de order (ontbreken in oudere bewaarde concepten: dan euro).
    orderCurrency?: string;
    orderRate?: number;
    orderRateSource?: string;
    orderRateDate?: string | null;
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
            orderCurrency: (order.currency ?? 'EUR').toUpperCase(), orderRate: order.exchangeRate ?? 1, orderRateSource: order.exchangeRateSource ?? 'NotApplicable',
            orderRateDate: order.exchangeRateDate ?? null,
        },
    }));
}

// Valuta-ronde: een factuur factureert alleen orders in haar eigen valuta (de server weigert anders). De valuta van de gekoppelde orders, of
// null zolang er niets gekoppeld is.
export function linkedOrderCurrency(draft: InvoiceDraft): string | null {
    const linked = draft.lines.find((line) => line.orderLine !== undefined && line.orderLine !== null);
    // Een oud concept zonder ordervaluta: voorlopig de valuta van de factuur zelf (het scherm haalt de order daarna opnieuw op, zie withOrderCurrency).
    return linked?.orderLine ? (linked.orderLine.orderCurrency ?? draft.header.currency).trim().toUpperCase() : null;
}

// Gekoppelde regels uit een bewaard concept van vóór de valuta-ronde hebben geen ordervaluta: de ids van die orders.
export function ordersMissingCurrency(draft: InvoiceDraft): number[] {
    return [...new Set(draft.lines.filter((line) => line.orderLine && line.orderLine.orderCurrency === undefined).map((line) => line.orderLine!.orderId))];
}

// Vult de valuta en koers van de order in op de regels van die order (uit de opnieuw opgehaalde order); de rest van het concept blijft.
// Staat de factuur in een andere valuta dan die order, dan gaat de factuur over op de valuta van de order (met een nieuwe koers), net als
// bij het koppelen van een eerste order: de overgenomen regels staan in de valuta van de order. Zo blijft er geen vergrendelde,
// verkeerde factuurvaluta staan die de server daarna weigert.
export function withOrderCurrency(draft: InvoiceDraft, order: Pick<InvoiceCandidate, 'id' | 'currency' | 'exchangeRate' | 'exchangeRateSource' | 'exchangeRateDate'>): InvoiceDraft {
    const currency = (order.currency ?? 'EUR').trim().toUpperCase();
    if (!draft.lines.some((line) => line.orderLine?.orderId === order.id && line.orderLine.orderCurrency === undefined)) return draft;
    const filled: InvoiceDraft = {
        ...draft,
        lines: draft.lines.map((line) => line.orderLine && line.orderLine.orderId === order.id && line.orderLine.orderCurrency === undefined
            ? { ...line, orderLine: { ...line.orderLine, orderCurrency: currency, orderRate: order.exchangeRate ?? 1,
                orderRateSource: order.exchangeRateSource ?? 'NotApplicable', orderRateDate: order.exchangeRateDate ?? null } }
            : line),
    };
    // Handmatige regels met bedragen: hun valuta verandert niet ongemerkt. Het conflict blijft zichtbaar (orderCurrencyConflict) en de
    // gebruiker lost het zelf op; opslaan wacht zolang (purchaseInvoiceRules).
    const manual = draft.lines.some((line) => line.orderLine === undefined && !isBlankLine(line));
    if (draft.header.currency.trim().toUpperCase() === currency || manual) return filled;
    return { ...filled, header: { ...filled.header, currency, exchangeRate: '', rateSource: undefined, rateDate: null }, touched: { ...filled.touched, currency: true } };
}

// Een gekoppelde order in een andere valuta dan de factuur (alleen mogelijk bij een hersteld oud concept met handmatige regels): de
// valuta van die order, anders null. Het valutaveld gaat dan open, zodat de gebruiker de valuta van de order kiest of de orderregels weghaalt.
export function orderCurrencyConflict(draft: InvoiceDraft): string | null {
    const linked = linkedOrderCurrency(draft);
    return linked !== null && linked !== draft.header.currency.trim().toUpperCase() ? linked : null;
}

// De valuta waarin de factuur vastzit: die van de gekoppelde orders, of anders die van de factuur zelf zodra er handmatig ingevoerde regels
// staan (die bedragen mogen niet ongemerkt in een andere valuta gaan gelden). null = nog vrij.
export function lockedInvoiceCurrency(draft: InvoiceDraft): string | null {
    const linked = linkedOrderCurrency(draft);
    if (linked !== null) return linked;
    return draft.lines.every(isBlankLine) ? null : draft.header.currency.trim().toUpperCase();
}

// Kan deze order bij deze factuur? Nee als de factuur al in een andere valuta vastzit (gekoppelde order of bestaande regels).
export function orderCurrencyFits(draft: InvoiceDraft, order: Pick<InvoiceCandidate, 'currency'>): boolean {
    const locked = lockedInvoiceCurrency(draft);
    return locked === null || locked === (order.currency ?? 'EUR').toUpperCase();
}

// Orderregels toevoegen: een leeg begin (alleen lege regels) wordt vervangen; een orderregel die er al staat komt er niet nog eens bij.
// Valuta-ronde: de eerste gekoppelde order bepaalt de valuta van de factuur (de koers wordt dan opnieuw die van de factuurdatum); een order in
// een andere valuta dan wat al gekoppeld is of wat de handmatige regels al hebben, komt er niet bij. Zonder nieuwe regels verandert er niets.
export function addOrderLines(draft: InvoiceDraft, order: InvoiceCandidate, defaultVat: number): InvoiceDraft {
    if (!orderCurrencyFits(draft, order)) return draft;
    const present = new Set(draft.lines.map((line) => line.orderLine?.lineId).filter((id) => id !== undefined));
    const added = linesFromCandidate(order, defaultVat).filter((line) => !present.has(line.orderLine!.lineId));
    if (added.length === 0) return draft;
    const kept = draft.lines.every(isBlankLine) ? [] : draft.lines;
    const currency = (order.currency ?? 'EUR').toUpperCase();
    const withCurrency = linkedOrderCurrency(draft) === null && currency !== draft.header.currency.trim().toUpperCase()
        ? { ...draft, header: { ...draft.header, currency, exchangeRate: '', rateSource: undefined, rateDate: null }, touched: { ...draft.touched, currency: true } }
        : draft;
    return { ...withCurrency, lines: [...kept, ...added] };
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
        // Vergeleken met de prijs na korting zonder afronding: 0,01% korting op 13,50 (13,49865) is een verschil, ook al toont het 13,50.
        differs: price !== null && discount !== null && (link.orderUnitPrice === null || Math.abs(price * (1 - discount / 100) - link.orderUnitPrice) > 1e-9),
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
