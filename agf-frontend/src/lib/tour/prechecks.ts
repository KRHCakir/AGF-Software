// Voorcontroles van de hoofdstukken: gewone GET-aanroepen die nagaan of de demo-database klaarstaat, en de waarden die
// het script nodig heeft (snelcode, eenheid, de open demo-inkooporder). Zo is elk hoofdstuk los te starten en zegt het
// anders precies wat er ontbreekt. De zoekregels zijn die van de schermen zelf (searchVendors), zodat Tab dezelfde keuze maakt.

import { todayInAmsterdam } from '../calendarDate';
import { formatDecimalInput } from '../decimal';
import { articleVariants } from '../receiptBlindEntry';
import { searchCustomers } from '../customerSearch';
import { addDays, searchVendors } from '../purchaseInvoiceRules';
import { getPurchaseOrder, listPurchaseOrders } from '../../services/purchaseOrderService';
import { getAvailabilityOverview } from '../../services/availabilityService';
import { getCustomers, getProductsWithVariants, getVendors } from '../../services/referenceDataService';
import type { AvailabilityOverview } from '../../types/availability';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import type { CustomerOption, ProductOption, VariantOption, VendorOption } from '../../types/referenceData';
import { TOUR_REF_PREFIX } from './types';
import type { TourMissing, TourPrecheck } from './types';

export const TOUR_VENDOR_SEARCH = 'Nature';
export const TOUR_PRODUCT = 'Avocado';
export const TOUR_VARIANT = 'Hass';
export const TOUR_ORDER_QUANTITY = 20;
export const TOUR_CUSTOMER_SEARCH = 'Lo';
export const TOUR_SALES_QUANTITY = 4;

// Tab neemt alleen een duidelijke suggestie over: de zoekterm moet precies één actieve leverancier vinden.
export function findTourVendor(vendors: readonly VendorOption[], search: string): VendorOption | TourMissing {
    const matches = searchVendors(vendors.filter((vendor) => vendor.isActive), search);
    if (matches.length === 1) return matches[0];
    return { key: matches.length === 0 ? 'tour_missing_vendor' : 'tour_missing_vendor_unique', params: { search } };
}

export interface TourArticle {
    variantId: number;
    code: string;
    unit: string;
    kgPerPackage: number;
}

// Avocado Hass met snelcode (die kiest Tab zonder twijfel), verpakkingseenheid en kg per verpakking (voor de rekenregel).
export function findTourArticle(products: readonly ProductOption[]): TourArticle | TourMissing {
    for (const product of products) {
        if (product.name.trim().toLowerCase() !== TOUR_PRODUCT.toLowerCase()) continue;
        const variants = [...product.variants].sort((a, b) => a.id - b.id);
        for (const variant of variants) {
            if (!variant.name.toLowerCase().startsWith(TOUR_VARIANT.toLowerCase())) continue;
            if (variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage) {
                return { variantId: variant.id, code: variant.shortcutCode, unit: variant.packagingUnit, kgPerPackage: variant.defaultNetKgPerPackage };
            }
        }
    }
    return { key: 'tour_missing_article', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } };
}

// De nieuwste demo-inkooporder (DEMO-TOUR-…) waar nog niets van ontvangen is.
export function findOpenTourOrder(items: readonly PurchaseOrderListItem[]): PurchaseOrderListItem | null {
    return [...items]
        .filter((item) => item.status === 'Ordered' && (item.supplierReference ?? '').startsWith(TOUR_REF_PREFIX))
        .sort((a, b) => b.id - a.id)[0] ?? null;
}

const isMissing = (value: object): value is TourMissing => 'key' in value;

export async function precheckPurchase(signal: AbortSignal): Promise<TourPrecheck> {
    const [vendors, products] = await Promise.all([getVendors(signal), getProductsWithVariants(signal)]);
    const vendor = findTourVendor(vendors, TOUR_VENDOR_SEARCH);
    const article = findTourArticle(products);
    if (isMissing(vendor) || isMissing(article)) return { missing: [vendor, article].filter(isMissing) };
    const today = todayInAmsterdam();
    return {
        missing: [],
        vars: {
            vendorSearch: TOUR_VENDOR_SEARCH,
            productCode: article.code,
            productUnit: article.unit,
            quantity: String(TOUR_ORDER_QUANTITY),
            expectedDate: addDays(today, 2) ?? today,
        },
    };
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
        tradeClass: variant.tradeClass ?? '',
        brand: variant.brand ?? '',
        // Berekend: kg per verpakking (etiket); gewogen: het totaal. Het andere veld blijft leeg en de actie wordt overgeslagen.
        kgPerPackage: calculated ? formatDecimalInput(kgPerPackage) : '',
        netKg: calculated ? '' : formatDecimalInput(Math.round(quantity * kgPerPackage * 1000) / 1000),
    };
}

// Een andere variant van hetzelfde artikel om een afwijking te laten zien: bij voorkeur alleen een andere herkomst.
export function findDeviationVariant(ordered: VariantOption, variants: readonly VariantOption[]): VariantOption | null {
    const others = variants.filter((variant) => variant.id !== ordered.id).sort((a, b) => a.id - b.id);
    const same = (a: string | null | undefined, b: string | null | undefined) => (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase();
    return others.find((variant) => !same(variant.originCountryCode ?? variant.origin, ordered.originCountryCode ?? ordered.origin)
        && same(variant.sizeCategory, ordered.sizeCategory) && same(variant.packaging, ordered.packaging)) ?? others[0] ?? null;
}

async function openTourLine(signal: AbortSignal, minimum: number) {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'open', vendorId: null, page: 1, pageSize: 50 }, signal);
    const order = findOpenTourOrder(list.items);
    if (!order) return null;
    const detail = await getPurchaseOrder(order.id, signal);
    const line = detail.lines.find((candidate) => candidate.unit !== null && Number.isInteger(candidate.expectedQuantity) && candidate.expectedQuantity >= minimum);
    return line ? { order, line } : null;
}

const kgPerPackageOf = (line: { expectedNetWeightKg: number | null; expectedQuantity: number }, variant: VariantOption) =>
    line.expectedNetWeightKg !== null && line.expectedQuantity > 0 ? line.expectedNetWeightKg / line.expectedQuantity : variant.defaultNetKgPerPackage ?? null;

export async function precheckReceive(signal: AbortSignal): Promise<TourPrecheck> {
    const [open, products] = await Promise.all([openTourLine(signal, 2), getProductsWithVariants(signal)]);
    if (!open) return { missing: [{ key: 'tour_missing_open_order' }] };
    const { order, line } = open;
    const variants = articleVariants(products, line.productVariantId);
    const variant = variants.find((candidate) => candidate.id === line.productVariantId);
    const kg = variant ? kgPerPackageOf(line, variant) : null;
    if (!variant || kg === null) return { missing: [{ key: 'tour_missing_open_order' }] };
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
        },
    };
}

// Afwijking bij ontvangst: een deel van een open demo-inkooporder komt als een andere variant van hetzelfde artikel binnen.
export async function precheckDeviation(signal: AbortSignal): Promise<TourPrecheck> {
    const [open, products] = await Promise.all([openTourLine(signal, 2), getProductsWithVariants(signal)]);
    if (!open) return { missing: [{ key: 'tour_missing_open_order' }] };
    const { order, line } = open;
    const variants = articleVariants(products, line.productVariantId);
    const ordered = variants.find((candidate) => candidate.id === line.productVariantId);
    const other = ordered ? findDeviationVariant(ordered, variants) : null;
    const kg = ordered ? kgPerPackageOf(line, ordered) : null;
    if (!ordered || !other || kg === null) return { missing: [{ key: 'tour_missing_deviation_variant', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}` } }] };
    const quantity = Math.max(1, Math.floor(line.expectedQuantity / 2));
    return {
        missing: [],
        vars: {
            orderId: String(order.id),
            orderNumber: order.orderNumber,
            lineId: String(line.id),
            quantity: String(quantity),
            expectedOrigin: ordered.origin ?? '',
            ...receiptVars(other, quantity, kg),
        },
    };
}

// De klantzoeker neemt met Tab de bovenste suggestie: er moet minstens één actieve klant op de zoekterm passen.
export function findTourCustomer(customers: readonly CustomerOption[], search: string): CustomerOption | TourMissing {
    return searchCustomers(customers, search)[0] ?? { key: 'tour_missing_customer', params: { search } };
}

// Reserveren kan alleen op goedgekeurde magazijnvoorraad in dezelfde eenheid, na bestaande reserveringen.
export function hasSellableStock(overview: AvailabilityOverview, variantId: number, unit: string, quantity: number): boolean {
    return overview.variants.some((variant) => variant.productVariantId === variantId
        && variant.warehouseSupply.some((supply) => supply.unit === unit && supply.availableAfterReservations.quantity >= quantity));
}

export async function precheckSales(signal: AbortSignal): Promise<TourPrecheck> {
    const [customers, products] = await Promise.all([getCustomers(signal), getProductsWithVariants(signal)]);
    const customer = findTourCustomer(customers, TOUR_CUSTOMER_SEARCH);
    const article = findTourArticle(products);
    if (isMissing(customer) || isMissing(article)) return { missing: [customer, article].filter(isMissing) };
    const overview = await getAvailabilityOverview({ productVariantId: article.variantId, route: 'Warehouse' }, signal);
    if (!hasSellableStock(overview, article.variantId, article.unit, TOUR_SALES_QUANTITY)) {
        return { missing: [{ key: 'tour_missing_stock', params: { article: `${TOUR_PRODUCT} ${TOUR_VARIANT}`, quantity: String(TOUR_SALES_QUANTITY) } }] };
    }
    const today = todayInAmsterdam();
    return {
        missing: [],
        vars: {
            customerSearch: TOUR_CUSTOMER_SEARCH,
            customerName: customer.name,
            productCode: article.code,
            quantity: String(TOUR_SALES_QUANTITY),
            deliveryDate: addDays(today, 3) ?? today,
        },
    };
}
