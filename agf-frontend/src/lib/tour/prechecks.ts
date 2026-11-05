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
import { getAvailabilityOverview } from '../../services/availabilityService';
import { getCountries } from '../../services/countryService';
import { getNextPartijNumber } from '../../services/partijService';
import { getCustomers, getProductsWithVariants, getVendors } from '../../services/referenceDataService';
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
// De afwijking bij ontvangst: een ander land van herkomst dan besteld.
export const TOUR_OTHER_ORIGIN = 'CL';
export const TOUR_ORDER_QUANTITY = 20;
export const TOUR_SALES_QUANTITY = 4;
// Afwijkend gewicht: 5% meer kg per colli dan besteld (de grens van "Klopt dit?" is 2%).
export const TOUR_WEIGHT_FACTOR = 1.05;

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

// Blinde invoer: wat de rondleiding bij ontvangst invult voor deze variant (alleen de kenmerken die het scherm vraagt; een lege waarde
// slaat de actie over via `when`) en het juiste kg-veld met het kg per verpakking.
export function receiptVars(variant: VariantOption, quantity: number, kgPerPackage: number): Record<string, string> {
    const calculated = variant.weightMethod === 'Calculated';
    return {
        originText: variant.originCountryCode ?? variant.origin ?? '',
        originName: variant.origin ?? '',
        size: variant.sizeCategory ?? '',
        packaging: variant.packaging ?? '',
        tradeClass: normalizeTradeClass(variant.tradeClass) ?? variant.tradeClass ?? '',
        brand: variant.brand ?? '',
        // Berekend: kg per verpakking (etiket); gewogen: het totaal. Het andere veld blijft leeg en de actie wordt overgeslagen.
        kgPerPackage: calculated ? formatDecimalInput(kgPerPackage) : '',
        netKg: calculated ? '' : formatDecimalInput(Math.round(quantity * kgPerPackage * 1000) / 1000),
    };
}

const same = (a: string | null | undefined, b: string | null | undefined) => (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase();

// De variant die bij een afwijkende herkomst hoort: zelfde artikel, maat, verpakking, klasse en merk, alleen een ander land.
export function findOriginVariant(ordered: VariantOption, variants: readonly VariantOption[], originCode: string): VariantOption | null {
    return variants.find((variant) => variant.id !== ordered.id && same(variant.originCountryCode, originCode) && same(variant.sizeCategory, ordered.sizeCategory)
        && same(variant.packaging, ordered.packaging) && same(normalizeTradeClass(variant.tradeClass), normalizeTradeClass(ordered.tradeClass)) && same(variant.brand, ordered.brand)) ?? null;
}

async function openTourLine(signal: AbortSignal, minimum: number) {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'open', vendorId: null, page: 1, pageSize: 50 }, signal);
    const order = findOpenTourOrder(list.items);
    if (!order) return null;
    const detail = await getPurchaseOrder(order.id, signal);
    const line = detail.lines.find((candidate) => candidate.unit !== null && Number.isInteger(candidate.expectedQuantity) && candidate.expectedQuantity >= minimum);
    return line ? { order, line } : null;
}

// Partij van de ontvangst: heeft de orderregel een partij, dan neemt de ontvangst die over (niets te typen) en heten de regels in de
// keuring zo; anders typt de rondleiding haar eigen kenmerk (de runner vult lot dan met tourRef).
export function lotVars(line: { partijNumber?: string | null }): Record<string, string> {
    return line.partijNumber ? { lot: line.partijNumber, typeLot: '', partijNumber: line.partijNumber } : { typeLot: 'yes', partijNumber: '' };
}

const kgPerPackageOf = (line: { expectedNetWeightKg: number | null; expectedQuantity: number }, variant: VariantOption) =>
    line.expectedNetWeightKg !== null && line.expectedQuantity > 0 ? line.expectedNetWeightKg / line.expectedQuantity : variant.defaultNetKgPerPackage ?? null;

const missingOpenOrder: TourMissing = { key: 'tour_missing_open_order' };

// Ontvangen: de open demo-inkooporder (die het hoofdstuk zo nodig net zelf heeft aangemaakt) en wat de rondleiding blind invult.
export async function lookupReceive(_vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const [open, products] = await Promise.all([openTourLine(signal, 2), getProductsWithVariants(signal)]);
    if (!open) return { missing: [missingOpenOrder] };
    const { order, line } = open;
    const variant = articleVariants(products, line.productVariantId).find((candidate) => candidate.id === line.productVariantId);
    const kg = variant ? kgPerPackageOf(line, variant) : null;
    if (!variant || kg === null) return { missing: [missingOpenOrder] };
    // Bij ontvangst zijn maat en klasse verplicht: een ouder artikel zonder die gegevens eerst aanvullen (Artikelen, Opschonen).
    if (!isCompleteVariant(variant)) return { missing: [{ key: 'tour_missing_complete_article', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } }] };
    return {
        missing: [],
        vars: {
            orderId: String(order.id),
            orderNumber: order.orderNumber,
            lineId: String(line.id),
            variantId: String(line.productVariantId),
            quantity: String(line.expectedQuantity),
            rejectQuantity: String(Math.max(1, Math.floor(line.expectedQuantity / 4))),
            ...receiptVars(variant, line.expectedQuantity, kg),
            ...lotVars(line),
        },
    };
}

// Hoofdstukken die een open demo-inkooporder ontvangen: zonder die order maken ze eerst stamgegevens en een order aan.
async function precheckOpenOrder(signal: AbortSignal, needs: { customer?: boolean }): Promise<TourPrecheck> {
    const [data, open] = await Promise.all([masterData(signal), openTourLine(signal, 2)]);
    const vars = { ...masterFlags(data, needs), needOrder: flag(open === null) };
    return { missing: [], vars };
}

export const precheckReceive = (signal: AbortSignal) => precheckOpenOrder(signal, {});

// Afwijking bij ontvangst: de helft van de open demo-order komt binnen uit een ander land en met 5% meer kg per colli. Bestaat die
// variant nog niet, dan maakt de ontvangst hem aan ("Artikel aanmaken en doorgaan"); daarna vraagt het systeem "Klopt dit?".
export async function lookupDeviation(vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const [receive, products, countries] = await Promise.all([lookupReceive(vars, signal), getProductsWithVariants(signal), getCountries(signal)]);
    if (receive.missing.length > 0 || !receive.vars) return receive;
    const variantId = Number(receive.vars.variantId);
    const variants = articleVariants(products, variantId);
    const ordered = variants.find((candidate) => candidate.id === variantId)!;
    const otherCode = same(ordered.originCountryCode, TOUR_OTHER_ORIGIN) ? TOUR_ORIGIN : TOUR_OTHER_ORIGIN;
    const otherName = countries.countries.find((country) => country.code === otherCode)?.nameNl ?? otherCode;
    const existing = findOriginVariant(ordered, variants, otherCode);
    const quantity = Math.max(1, Math.floor(Number(receive.vars.quantity) / 2));
    const expectedKg = (ordered.defaultNetKgPerPackage ?? 0) > 0 ? ordered.defaultNetKgPerPackage! : Number(receive.vars.kgPerPackage.replace(',', '.'));
    const heavier = Math.round(expectedKg * TOUR_WEIGHT_FACTOR * 10) / 10;
    return {
        missing: [],
        vars: {
            ...receive.vars,
            quantity: String(quantity),
            expectedOrigin: ordered.origin ?? '',
            ...receiptVars({ ...ordered, originCountryCode: otherCode, origin: otherName }, quantity, heavier),
            createVariant: flag(existing === null),
        },
    };
}

export const precheckDeviation = (signal: AbortSignal) => precheckOpenOrder(signal, {});

// Na de afwijkende ontvangst: de variant waar de voorraad nu op staat (de exacte variant, niet de bestelde).
export async function lookupDeviationStock(vars: Readonly<Record<string, string>>, signal: AbortSignal): Promise<TourPrecheck> {
    const products = await getProductsWithVariants(signal);
    const variants = articleVariants(products, Number(vars.variantId));
    const ordered = variants.find((candidate) => candidate.id === Number(vars.variantId));
    const actual = ordered ? findOriginVariant(ordered, variants, vars.originText) : null;
    return actual ? { missing: [], vars: { actualVariantId: String(actual.id) } } : { missing: [{ key: 'tour_missing_deviation_variant', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } }] };
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
