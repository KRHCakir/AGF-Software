// Voorcontroles van de hoofdstukken: gewone GET-aanroepen die nagaan of de demo-database klaarstaat, en de waarden die
// het script nodig heeft (snelcode, eenheid, de open demo-inkooporder). Zo is elk hoofdstuk los te starten en zegt het
// anders precies wat er ontbreekt. De zoekregels zijn die van de schermen zelf (searchVendors), zodat Tab dezelfde keuze maakt.

import { todayInAmsterdam } from '../calendarDate';
import { addDays, searchVendors } from '../purchaseInvoiceRules';
import { getPurchaseOrder, listPurchaseOrders } from '../../services/purchaseOrderService';
import { getProductsWithVariants, getVendors } from '../../services/referenceDataService';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder';
import type { ProductOption, VendorOption } from '../../types/referenceData';
import { TOUR_REF_PREFIX } from './types';
import type { TourMissing, TourPrecheck } from './types';

export const TOUR_VENDOR_SEARCH = 'Nature';
export const TOUR_PRODUCT = 'Avocado';
export const TOUR_VARIANT = 'Hass';
export const TOUR_ORDER_QUANTITY = 20;

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

export async function precheckReceive(signal: AbortSignal): Promise<TourPrecheck> {
    const list = await listPurchaseOrders({ query: TOUR_REF_PREFIX, status: 'open', vendorId: null, page: 1, pageSize: 50 }, signal);
    const order = findOpenTourOrder(list.items);
    if (!order) return { missing: [{ key: 'tour_missing_open_order' }] };
    const detail = await getPurchaseOrder(order.id, signal);
    const line = detail.lines.find((candidate) => candidate.unit !== null && Number.isInteger(candidate.expectedQuantity) && candidate.expectedQuantity >= 2);
    if (!line) return { missing: [{ key: 'tour_missing_open_order' }] };
    return {
        missing: [],
        vars: {
            orderId: String(order.id),
            orderNumber: order.orderNumber,
            lineId: String(line.id),
            variantId: String(line.productVariantId),
            quantity: String(line.expectedQuantity),
            rejectQuantity: String(Math.max(1, Math.floor(line.expectedQuantity / 4))),
        },
    };
}
