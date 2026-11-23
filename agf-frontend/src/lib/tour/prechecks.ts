// Voorcontroles van de hoofdstukken: gewone GET-aanroepen die nagaan wat er al is, en de waarden die het script nodig heeft (zoekcode,
// snelcode, eenheid, de open demo-inkooporder). De rondleiding werkt met haar eigen voorbeeldgegevens (DEMO-TOUR-…): ontbreken die, dan
// zet de voorcontrole een vlag (needVendor, needOrder …) en maakt het hoofdstuk ze eerst zelf aan via de schermen. Zo is elk hoofdstuk
// los te starten, ook op een lege database. Na het aanmaken lezen de stappen opnieuw (lookup*), met dezelfde zoekregels als de schermen.

import { formatDateInput, todayInAmsterdam } from '../calendarDate';
import { formatDecimalInput } from '../decimal';
import { articleVariants, isCompleteVariant } from '../receiptBlindEntry';
import { normalizeTradeClass } from '../tradeClass';
import { searchCustomers } from '../customerSearch';
import { addDays, searchVendors } from '../purchaseInvoiceRules';
import { getPurchaseOrder, listPurchaseOrders } from '../../services/purchaseOrderService';
import { getAiReadingStatus } from '../../services/aiReadingService';
import { listSalesOrders } from '../../services/salesOrderService';
import { getAvailabilityOverview } from '../../services/availabilityService';
import { getCountries } from '../../services/countryService';
import { getNextPartijNumber } from '../../services/partijService';
import { getCustomers, getProductsWithVariants, getVendors } from '../../services/referenceDataService';
import { listRelations } from '../../services/relationService';
import type { AvailabilityOverview } from '../../types/availability';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import type { CustomerOption, ProductOption, VariantOption, VendorOption } from '../../types/referenceData';
import { TOUR_REF_PREFIX } from './types';
import type { TourMissing, TourPrecheck } from './types';

// Het rondleidingsartikel: Avocado Hass uit Peru, maat 18, klasse I, 4 kg doos, met het DEMO-TOUR-kenmerk als merk en snelcode. Het
// merk maakt het een eigen artikel naast een bestaande Avocado Hass, zodat de blinde invoer bij ontvangst altijd precies dit vindt.
export const TOUR_PRODUCT = 'Avocado';
export const TOUR_VARIANT = 'Hass';
export const TOUR_ORIGIN = 'PE';
// Iets anders ontvangen: een ander land van herkomst dan besteld.
export const TOUR_OTHER_ORIGIN = 'ZA';
export const TOUR_ORDER_QUANTITY = 20;
export const TOUR_SALES_QUANTITY = 4;

const isTour = (value: string | null | undefined) => (value ?? '').startsWith(TOUR_REF_PREFIX);
const newestFirst = <T extends { id: number }>(items: readonly T[]) => [...items].sort((a, b) => b.id - a.id);
const flag = (needed: boolean) => (needed ? 'yes' : '');

// De nieuwste actieve DEMO-TOUR-leverancier waarop zoeken (zoekcode, anders naam) precies één leverancier vindt: dan kiest Tab haar.
export function findTourVendor(vendors: readonly VendorOption[]): { vendor: VendorOption; search: string } | null {
    const active = vendors.filter((vendor) => vendor.isActive);
    for (const vendor of newestFirst(active.filter((candidate) => isTour(candidate.searchCode) || isTour(candidate.name)))) {
        const search = vendor.searchCode ?? vendor.name;
        const matches = searchVendors(active, search);
        if (matches.length === 1 && matches[0].id === vendor.id) return { vendor, search };
    }
    return null;
}

// De nieuwste actieve DEMO-TOUR-klant die bovenaan staat als je op haar zoekcode zoekt (Tab neemt de bovenste suggestie).
export function findTourCustomer(customers: readonly CustomerOption[]): { customer: CustomerOption; search: string } | null {
    for (const customer of newestFirst(customers.filter((candidate) => candidate.isActive && (isTour(candidate.searchCode) || isTour(candidate.name))))) {
        const search = customer.searchCode ?? customer.name;
        if (searchCustomers(customers, search)[0]?.id === customer.id) return { customer, search };
    }
    return null;
}

export interface TourArticle {
    variantId: number;
    code: string;
    unit: string;
    kgPerPackage: number;
}

// Het nieuwste niet-gearchiveerde rondleidingsartikel (merk DEMO-TOUR-…) met snelcode, eenheid, kg per verpakking, maat en klasse.
export function findTourArticle(products: readonly ProductOption[]): TourArticle | null {
    const product = products.find((candidate) => candidate.name.trim().toLowerCase() === TOUR_PRODUCT.toLowerCase());
    for (const variant of newestFirst(product?.variants ?? [])) {
        if (variant.isArchived || !isTour(variant.brand) || variant.name.trim().toLowerCase() !== TOUR_VARIANT.toLowerCase()) continue;
        if (variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage && isCompleteVariant(variant)) {
            return { variantId: variant.id, code: variant.shortcutCode, unit: variant.packagingUnit, kgPerPackage: variant.defaultNetKgPerPackage };
        }
    }
    return null;
}

// De nieuwste demo-inkooporder (DEMO-TOUR-…) waar nog niets van ontvangen is.
export function findOpenTourOrder(items: readonly PurchaseOrderListItem[]): PurchaseOrderListItem | null {
    return newestFirst(items.filter((item) => item.status === 'Ordered' && isTour(item.supplierReference)))[0] ?? null;
}

const missingVendor: TourMissing = { key: 'tour_missing_vendor' };
const missingCustomer: TourMissing = { key: 'tour_missing_customer' };
const missingArticle: TourMissing = { key: 'tour_missing_article', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } };

interface MasterData {
    vendor: ReturnType<typeof findTourVendor>;
    customer: ReturnType<typeof findTourCustomer>;
    article: TourArticle | null;
    products: ProductOption[];
}

async function masterData(signal: AbortSignal): Promise<MasterData> {
    const [vendors, customers, products] = await Promise.all([getVendors(signal), getCustomers(signal), getProductsWithVariants(signal)]);
    return { vendor: findTourVendor(vendors), customer: findTourCustomer(customers), article: findTourArticle(products), products };
}

// Wat het hoofdstuk eerst zelf moet aanmaken (stappen van Stamgegevens met `when`).
function masterFlags(data: MasterData, needs: { customer?: boolean }): Record<string, string> {
    return { needVendor: flag(data.vendor === null), needCustomer: flag(!!needs.customer && data.customer === null), needArticle: flag(data.article === null) };
}

// Zoals een gebruiker typt: de verwachte datum met tweecijferig jaar (04-11-26), die het datumveld aanvult tot 04-11-2026.
export function shortDate(date: string): string {
    const full = formatDateInput(date);
    return full.replace(/^(\d{2}-\d{2}-)\d{2}(\d{2})$/, '$1$2');
}

// Inkooporder aanmaken: leverancier en artikel van de rondleiding, en of de partijnummering een voorstel geeft.
// Een eerder ingevuld getal van de rondleiding teruglezen, met de gedeelde parser (nooit een eigen komma-puntvertaling).

export async function lookupPurchase(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const [data, numbering] = await Promise.all([masterData(signal), getNextPartijNumber(undefined, signal)]);
    if (data.vendor === null || data.article === null) return { missing: [...(data.vendor === null ? [missingVendor] : []), ...(data.article === null ? [missingArticle] : [])] };
    const today = todayInAmsterdam();
    const expected = addDays(today, 2) ?? today;
    return {
        missing: [],
        vars: {
            vendorSearch: data.vendor.search,
            vendorName: data.vendor.vendor.name,
            productCode: data.article.code,
            productUnit: data.article.unit,
            quantity: String(TOUR_ORDER_QUANTITY),
            expectedDate: shortDate(expected),
            expectedDateFull: formatDateInput(expected),
            // Nummering op Eigen (geen voorstel): de rondleiding typt haar eigen kenmerk als partijnummer.
            ownNumbering: numbering.suggestion === null ? 'yes' : '',
        },
    };
}

export async function precheckPurchase(signal: AbortSignal): Promise<TourPrecheck> {
    const flags = masterFlags(await masterData(signal), {});
    return { missing: [], vars: flags };
}

const same = (a: string | null | undefined, b: string | null | undefined) => (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase();

// De variant die bij een afwijkende herkomst hoort: zelfde artikel, maat, verpakking, klasse en merk, alleen een ander land.
export function findOriginVariant(ordered: VariantOption, variants: readonly VariantOption[], originCode: string): VariantOption | null {
    return variants.find((variant) => variant.id !== ordered.id && same(variant.originCountryCode, originCode) && same(variant.sizeCategory, ordered.sizeCategory)
        && same(variant.packaging, ordered.packaging) && same(normalizeTradeClass(variant.tradeClass), normalizeTradeClass(ordered.tradeClass)) && same(variant.brand, ordered.brand)) ?? null;
}

// editable: alleen een order die nog Open is (documentstatus), voor hoofdstukken die de order daarna aanpassen.
async function openTourLine(signal: AbortSignal, minimum: number, editable = false) {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'open', vendorId: null, page: 1, pageSize: 50, ...(editable ? { document: 'Open' } : {}) }, signal);
    const order = findOpenTourOrder(list.items);
    if (!order) return null;
    const detail = await getPurchaseOrder(order.id, signal);
    // Het openstaande aantal (besteld min al ontvangen) telt, niet het bestelde aantal; de index is die van de regel in de order.
    const openOf = (candidate: (typeof detail.lines)[number]) => candidate.expectedQuantity - (candidate.receivedQuantity ?? 0);
    // Alleen een regel met bekend gewicht: dan vraagt de ontvangst geen netto kg (review: oudere demo-orders zonder kg zouden vastlopen).
    const lineIndex = detail.lines.findIndex((candidate) => candidate.unit !== null && candidate.expectedNetWeightKg != null && Number.isInteger(candidate.expectedQuantity)
        && Number.isInteger(openOf(candidate)) && openOf(candidate) >= minimum);
    // Aanpassen (splitsen): het artikelveld van de eerste regel; een order waarvan alleen een latere regel past, slaat het hoofdstuk over.
    if (lineIndex < 0 || (editable && lineIndex !== 0)) return null;
    return { order, line: detail.lines[lineIndex], lineIndex, openQuantity: openOf(detail.lines[lineIndex]) };
}

const missingOpenOrder: TourMissing = { key: 'tour_missing_open_order' };

// Ontvangen (feedback 22 november: één scherm, één klik per regel): de open demo-inkooporder (die het hoofdstuk zo nodig net zelf heeft
// aangemaakt), de regel en het aantal; het lot is dat van de ontvangst: de partij van de regel, anders het lot van de regel, van de
// leverancier of het ordernummer (zoals ReceiptService).
export const lookupReceive = (vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> => receiveLookup(signal, false);

async function receiveLookup(signal: AbortSignal, editable: boolean): Promise<TourPrecheck> {
    const open = await openTourLine(signal, 2, editable);
    if (!open) return { missing: [missingOpenOrder] };
    const { order, line, lineIndex, openQuantity } = open;
    const detail = await getPurchaseOrder(order.id, signal);
    const lot = line.partijNumber ?? line.lotNumber ?? detail.supplierLotReference ?? order.orderNumber;
    return {
        missing: [],
        vars: {
            orderId: String(order.id),
            orderNumber: order.orderNumber,
            lineId: String(line.id),
            lineIndex: String(lineIndex),
            variantId: String(line.productVariantId),
            quantity: String(openQuantity),
            rejectQuantity: String(Math.max(1, Math.floor(openQuantity / 4))),
            lot,
            partijNumber: line.partijNumber ?? '',
        },
    };
}

// Na Ontvangen (prompt 5): een order zonder partij krijgt bij de eerste ontvangst een I-nummer, en de pallet staat onder dat nummer
// in de keuring. Het lot is dan de partij die de regel nu heeft; anders blijft het lot van vóór de ontvangst staan.
export async function lookupReceivedLot(vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const detail = await getPurchaseOrder(Number(vars.orderId), signal);
    const line = detail.lines.find((candidate) => String(candidate.id) === vars.lineId);
    const partijNumber = line?.partijNumber ?? null;
    return { missing: [], vars: partijNumber ? { lot: partijNumber, partijNumber } : {} };
}

// Hoofdstukken die een open demo-inkooporder ontvangen: zonder die order maken ze eerst stamgegevens en een order aan.
async function precheckOpenOrder(signal: AbortSignal, needs: { customer?: boolean }): Promise<TourPrecheck> {
    const [data, open] = await Promise.all([masterData(signal), openTourLine(signal, 2)]);
    const vars = { ...masterFlags(data, needs), needOrder: flag(open === null) };
    return { missing: [], vars };
}

export const precheckReceive = (signal: AbortSignal) => precheckOpenOrder(signal, {});

// Iets anders ontvangen (feedback 22 november): de helft van de open demo-order komt binnen, daarna blijkt de rest uit een ander land te
// komen. De gebruiker past de orderregel aan; omdat er al iets ontvangen is, splitst de regel. Het artikel uit het andere land maakt het
// hoofdstuk zo nodig eerst aan (zelfde artikel, maat, verpakking, klasse en merk; alleen het land verschilt).
async function otherOriginOf(variantId: number, signal: AbortSignal) {
    const [products, countries] = await Promise.all([getProductsWithVariants(signal), getCountries(signal)]);
    const variants = articleVariants(products, variantId);
    const ordered = variants.find((candidate) => candidate.id === variantId) ?? null;
    if (ordered === null) return null;
    const otherCode = same(ordered.originCountryCode, TOUR_OTHER_ORIGIN) ? TOUR_ORIGIN : TOUR_OTHER_ORIGIN;
    const otherName = countries.countries.find((country) => country.code === otherCode)?.nameNl ?? otherCode;
    return { ordered, otherCode, otherName, other: findOriginVariant(ordered, variants, otherCode) };
}

export async function precheckSplit(signal: AbortSignal): Promise<TourPrecheck> {
    const [data, open] = await Promise.all([masterData(signal), openTourLine(signal, 2, true)]);
    // Het artikel van de open order, anders het rondleidingsartikel waarmee het hoofdstuk de order aanmaakt.
    const variantId = open?.line.productVariantId ?? data.article?.variantId ?? null;
    const other = variantId === null ? null : await otherOriginOf(variantId, signal);
    return { missing: [], vars: { ...masterFlags(data, {}), needOrder: flag(open === null), needOtherArticle: flag(other?.other == null) } };
}

// Voor het aanmaken van het artikel uit het andere land: de kenmerken van het bestelde artikel (merk, snelcode) en het andere land.
export async function lookupOtherArticle(vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const receive = await receiveLookup(signal, true);
    if (receive.missing.length > 0 || !receive.vars) return receive;
    const found = await otherOriginOf(Number(receive.vars.variantId), signal);
    if (found === null) return { missing: [missingOpenOrder] };
    // Alle kenmerken van het bestelde artikel, behalve het land (review: anders vindt de splitsing het nieuwe artikel niet terug).
    const ordered = found.ordered;
    const number = (value: number | null | undefined) => (value === null || value === undefined ? '' : formatDecimalInput(value));
    return {
        missing: [],
        vars: {
            otherCode: found.otherCode, otherOrigin: found.otherName, orderedBrand: ordered.brand ?? '', otherShortcut: `${ordered.shortcutCode ?? vars.tourRef}-${found.otherCode}`,
            orderedSize: ordered.sizeCategory ?? '', orderedClass: normalizeTradeClass(ordered.tradeClass) ?? '', orderedPackaging: ordered.packaging ?? '',
            orderedUnit: ordered.packagingUnit ?? 'colli', orderedMethod: ordered.weightMethod ?? 'Calculated', orderedNetKg: number(ordered.defaultNetKgPerPackage),
            orderedPerPallet: number(ordered.defaultPackagesPerPallet),
        },
    };
}

export async function lookupSplit(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const receive = await receiveLookup(signal, true);
    if (receive.missing.length > 0 || !receive.vars) return receive;
    const found = await otherOriginOf(Number(receive.vars.variantId), signal);
    if (found?.other == null) return { missing: [{ key: 'tour_missing_deviation_variant', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } }] };
    const half = Math.max(1, Math.floor(Number(receive.vars.quantity) / 2));
    return {
        missing: [],
        vars: {
            ...receive.vars,
            half: String(half),
            rest: String(Number(receive.vars.quantity) - half),
            otherVariantId: String(found.other.id),
            otherSearch: found.other.shortcutCode ?? `${TOUR_PRODUCT} ${TOUR_VARIANT} ${found.otherName}`,
            otherOrigin: found.otherName,
        },
    };
}

// Order aanpassen en definitief maken (stap 4a): de nieuwste demo-inkooporder (DEMO-TOUR-…) die nog Open is, voor het eigen magazijn en
// niet geannuleerd; zonder zo'n order maakt het hoofdstuk er eerst een aan (stamgegevens en Inkooporder aanmaken).
async function editableTourOrder(signal: AbortSignal) {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'all', vendorId: null, page: 1, pageSize: 50, document: 'Open' }, signal);
    return newestFirst(list.items.filter((item) => isTour(item.supplierReference) && item.route === 'Warehouse' && item.status !== 'Cancelled'))[0] ?? null;
}

export async function precheckOrderEdit(signal: AbortSignal): Promise<TourPrecheck> {
    const [data, order] = await Promise.all([masterData(signal), editableTourOrder(signal)]);
    return { missing: [], vars: { ...masterFlags(data, {}), needOrder: flag(order === null) } };
}

export async function lookupOrderEdit(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const order = await editableTourOrder(signal);
    if (!order) return { missing: [missingOpenOrder] };
    const detail = await getPurchaseOrder(order.id, signal);
    if (detail.lines.length === 0) return { missing: [missingOpenOrder] };
    return { missing: [], vars: { orderId: String(order.id), orderNumber: order.orderNumber, orderRef: order.supplierReference ?? order.orderNumber, lineId: String(detail.lines[0].id) } };
}

// Reserveren kan alleen op goedgekeurde magazijnvoorraad in dezelfde eenheid, na bestaande reserveringen. Met de verkoopstand ook na de
// verkoop uit voorraad die nog niet gereserveerd is (vrij op voorraad), anders stelt de order splitsen voor.
export function hasSellableStock(overview: AvailabilityOverview, variantId: number, unit: string, quantity: number): boolean {
    return overview.variants.some((variant) => variant.productVariantId === variantId
        && variant.warehouseSupply.some((supply) => supply.unit === unit && supply.availableAfterReservations.quantity >= quantity
            && (supply.sales === undefined || supply.sales === null || supply.sales.freeStock >= quantity)));
}

// Hoeveel meer dan vrij te verkopen het hoofdstuk "Verkopen voor aankomst" verkoopt: zo ontstaat altijd een (klein) tekort.
export const TOUR_PRESELL_SHORTAGE = 5;

// Verkopen voor aankomst: de open demo-inkooporder, de klant van de rondleiding en het artikel van die regel. Er wordt vrij te verkopen
// + 5 verkocht uit verwachte aanvoer (tekort 5), daarna die order ontvangen en goedgekeurd, daarna oudste partij eerst gereserveerd.
export async function lookupPresell(vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const [receive, customers] = await Promise.all([lookupReceive(vars, signal), getCustomers(signal)]);
    if (receive.missing.length > 0 || !receive.vars) return receive;
    const customer = findTourCustomer(customers);
    if (customer === null) return { missing: [missingCustomer] };
    const variantId = Number(receive.vars.variantId);
    const open = await openTourLine(signal, 2);
    const unit = open?.line.unit ?? null;
    if (unit === null) return { missing: [missingOpenOrder] };
    const overview = await getAvailabilityOverview({ productVariantId: variantId, route: 'Warehouse' }, signal);
    const sales = overview.variants.filter((row) => row.productVariantId === variantId).flatMap((row) => row.warehouseSupply).find((row) => row.unit === unit)?.sales ?? null;
    const free = sales === null || !Number.isFinite(sales.free) ? 0 : Math.max(0, Math.floor(sales.free));
    const today = todayInAmsterdam();
    return {
        missing: [],
        vars: {
            ...receive.vars,
            unit,
            customerSearch: customer.search,
            customerName: customer.customer.name,
            sellQuantity: String(free + TOUR_PRESELL_SHORTAGE),
            deliveryDate: formatDateInput(addDays(today, 3) ?? today),
        },
    };
}

export const precheckPresell = (signal: AbortSignal) => precheckOpenOrder(signal, { customer: true });

async function sellableTourStock(signal: AbortSignal, article: TourArticle | null): Promise<boolean> {
    if (article === null) return false;
    const overview = await getAvailabilityOverview({ productVariantId: article.variantId, route: 'Warehouse' }, signal);
    return hasSellableStock(overview, article.variantId, article.unit, TOUR_SALES_QUANTITY);
}

// Verkooporder, reserveren en verzenden: de klant en het artikel van de rondleiding met goedgekeurde voorraad (zo nodig eerst een
// order ontvangen en keuren in dit hoofdstuk).
export async function lookupSales(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const data = await masterData(signal);
    if (data.customer === null || data.article === null) return { missing: [...(data.customer === null ? [missingCustomer] : []), ...(data.article === null ? [missingArticle] : [])] };
    if (!await sellableTourStock(signal, data.article)) {
        return { missing: [{ key: 'tour_missing_stock', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}`, quantity: String(TOUR_SALES_QUANTITY) } }] };
    }
    const today = todayInAmsterdam();
    return {
        missing: [],
        vars: {
            customerSearch: data.customer.search,
            customerName: data.customer.customer.name,
            productCode: data.article.code,
            salesQuantity: String(TOUR_SALES_QUANTITY),
            deliveryDate: formatDateInput(addDays(today, 3) ?? today),
        },
    };
}

export async function precheckSales(signal: AbortSignal): Promise<TourPrecheck> {
    const data = await masterData(signal);
    const needStock = !await sellableTourStock(signal, data.article);
    return { missing: [], vars: { ...masterFlags(data, { customer: true }), needOrder: flag(needStock), needStock: flag(needStock) } };
}

// De rode draad: een demo-inkooporder waaruit al verkocht is, met een van die verkooporders en de partij. Alleen lezen; is die er nog
// niet (lege database), dan maakt het hoofdstuk eerst stamgegevens, een order, een ontvangst en een verkoop aan.
export async function lookupChain(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'all', vendorId: null, page: 1, pageSize: 50 }, signal);
    for (const item of newestFirst(list.items)) {
        if (item.receivedQuantity <= 0) continue;
        const detail = await getPurchaseOrder(item.id, signal);
        const sale = (detail.salesOrders ?? [])[0];
        if (sale && detail.partijId) {
            return { missing: [], vars: { orderId: String(detail.id), orderNumber: detail.orderNumber, salesOrderId: String(sale.salesOrderId), partijId: String(detail.partijId), partijNumber: detail.partijNumber ?? '' } };
        }
    }
    return { missing: [{ key: 'tour_missing_chain' }] };
}

export async function precheckChain(signal: AbortSignal): Promise<TourPrecheck> {
    const chain = await lookupChain({}, signal);
    if (chain.missing.length === 0) return chain;
    const data = await masterData(signal);
    const vars = { ...masterFlags(data, { customer: true }), needOrder: 'yes', needStock: 'yes', needSale: 'yes' };
    return { missing: [], vars, writes: true };
}

// Kosten verdelen: de factuur is van de leverancier van de rondleiding, de kosten gaan naar de verkoop-partij van de nieuwste open
// DEMO-TOUR-verkooporder (één order, één klant). Alleen lezen.
export async function lookupCosts(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const data = await masterData(signal);
    if (data.vendor === null) return { missing: [missingVendor] };
    const orders = await listSalesOrders({ status: 'Open' }, signal);
    const order = orders.filter((candidate) => !!candidate.salesPartijId && !!candidate.salesPartijNumber && isTour(candidate.customerReference))
        .sort((a, b) => b.id - a.id)[0];
    if (!order) return { missing: [{ key: 'tour_missing_sales_lot' }] };
    return {
        missing: [],
        vars: { vendorSearch: data.vendor.search, vendorName: data.vendor.vendor.name, salesPartijId: String(order.salesPartijId), salesPartijNumber: order.salesPartijNumber! },
    };
}

// Zonder verkoop-partij van de rondleiding (lege database) maakt het hoofdstuk eerst stamgegevens, voorraad en een verkoop aan.
export async function precheckCosts(signal: AbortSignal): Promise<TourPrecheck> {
    const data = await masterData(signal);
    const found = await lookupCosts({}, signal);
    if (found.missing.length === 0) return { missing: [], vars: masterFlags(data, {}), writes: true };
    const needStock = !await sellableTourStock(signal, data.article);
    return { missing: [], vars: { ...masterFlags(data, { customer: true }), needOrder: flag(needStock), needStock: flag(needStock), needSale: 'yes' }, writes: true };
}

// Inkoopfactuur invoeren: de leverancier en het artikel van de rondleiding.
export async function lookupInvoice(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const data = await masterData(signal);
    if (data.vendor === null || data.article === null) return { missing: [...(data.vendor === null ? [missingVendor] : []), ...(data.article === null ? [missingArticle] : [])] };
    return { missing: [], vars: { vendorSearch: data.vendor.search, vendorName: data.vendor.vendor.name, productCode: data.article.code } };
}

export async function precheckInvoice(signal: AbortSignal): Promise<TourPrecheck> {
    return { missing: [], vars: masterFlags(await masterData(signal), {}) };
}

// Stamgegevens: altijd een nieuwe leverancier, klant en artikel (met het kenmerk van deze rondleiding); niets vooraf nodig.
export async function precheckMasterdata(): Promise<TourPrecheck> {
    return { missing: [], vars: { originSearch: TOUR_ORIGIN } };
}

// Factuur uploaden (stap 7a): alleen met de nagebootste lezer (DocumentAi:Reader = Demo), nooit met de echte AI; de leverancier van de
// rondleiding en een niet-geannuleerde demo-inkooporder (DEMO-TOUR-…) om het voorstel aan te koppelen. AI-lezen uit: de rondleiding zet het aan.
export async function precheckAiInvoice(signal: AbortSignal): Promise<TourPrecheck> {
    const [status, data, list] = await Promise.all([getAiReadingStatus(signal).catch(() => null), masterData(signal),
        listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'all', vendorId: null, page: 1, pageSize: 50 }, signal)]);
    if (status === null || status.provider !== 'Demo') return { missing: [{ key: 'tour_missing_demo_reader' }] };
    if (data.vendor === null) return { missing: [missingVendor] };
    const order = newestFirst(list.items.filter((item) => item.vendorId === data.vendor!.vendor.id && item.status !== 'Cancelled' && isTour(item.supplierReference)))[0];
    if (!order) return { missing: [{ key: 'tour_missing_ai_order' }] };
    return { missing: [], vars: { vendorName: data.vendor.vendor.name, orderRef: order.supplierReference!, aiOff: status.enabled ? '' : 'ja' } };
}

// Certificaat laten lezen (certificaten deel 2): alleen met de nagebootste lezer (nooit de echte AI), bij de leverancier van de rondleiding
// (de relatie wordt geopend met het blok Certificaten). De demolezer zet de naam van die leverancier als houder: geen waarschuwing.
export async function precheckAiCertificate(signal: AbortSignal): Promise<TourPrecheck> {
    const [status, data] = await Promise.all([getAiReadingStatus(signal).catch(() => null), masterData(signal)]);
    if (status === null || status.provider !== 'Demo') return { missing: [{ key: 'tour_missing_demo_reader' }] };
    if (data.vendor === null) return { missing: [missingVendor] };
    const vendor = data.vendor;
    const relations = await listRelations({ role: 'vendor', query: vendor.search, includeInactive: false, page: 1, pageSize: 25 }, signal);
    const relation = relations.items.find((item) => item.name === vendor.vendor.name && (item.searchCode ?? item.name) === vendor.search)
        ?? relations.items.find((item) => item.name === vendor.vendor.name);
    if (!relation) return { missing: [missingVendor] };
    return { missing: [], vars: { vendorName: vendor.vendor.name, vendorRelationId: String(relation.id), aiOff: status.enabled ? '' : 'ja' } };
}
