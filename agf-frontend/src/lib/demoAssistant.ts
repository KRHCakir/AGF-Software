// Pure regels van de demo-assistent, los van React en i18n, zodat ze met node:test te toetsen zijn.
// De assistent mag alleen navigeren, zoeken en uitleggen: vaste routes, een vaste rondleiding en zoeken in de
// bestaande orderlijst (GET). Er is geen AI en er wordt nooit iets gewijzigd.

export const DEMO_ROUTES = ['/supply', '/inspection', '/availability', '/sales-orders'] as const;
export type DemoRoute = (typeof DEMO_ROUTES)[number];

export interface TourStep {
    id: 'supply' | 'inspection' | 'availability' | 'sales';
    // Een vaste route, eventueel met de toegestane zoekterm (Keuring toont dan de DEMO-pallets, niet de oude rooktestpallets).
    href: string;
    titleKey: string;
    bodyKey: string;
}

export const TOUR_STEPS: readonly TourStep[] = [
    { id: 'supply', href: '/supply', titleKey: 'demo_step_supply_title', bodyKey: 'demo_step_supply_body' },
    { id: 'inspection', href: '/inspection?search=DEMO', titleKey: 'demo_step_inspection_title', bodyKey: 'demo_step_inspection_body' },
    { id: 'availability', href: '/availability', titleKey: 'demo_step_availability_title', bodyKey: 'demo_step_availability_body' },
    { id: 'sales', href: '/sales-orders', titleKey: 'demo_step_sales_title', bodyKey: 'demo_step_sales_body' },
];

export function tourStepIndexFor(pathname: string): number {
    return TOUR_STEPS.findIndex((step) => step.href.split('?')[0] === pathname);
}

const ORDER_LINK = /^\/sales-orders\?orderId=[1-9]\d{0,9}$/;
const INSPECTION_SEARCH_LINK = /^\/inspection\?search=[A-Za-z0-9-]{1,40}$/;

// Alleen een vaste route, een keuringszoekterm of een link naar één verkooporder; nooit externe URL's, scripts of andere paden.
export function isAllowedDemoHref(href: string): boolean {
    return (DEMO_ROUTES as readonly string[]).includes(href) || ORDER_LINK.test(href) || INSPECTION_SEARCH_LINK.test(href);
}

export function orderHref(id: number): string {
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error(`Invalid order id: ${id}`);
    return `/sales-orders?orderId=${id}`;
}

export interface FindableOrder {
    id: number;
    customerName: string | null;
    customerReference: string | null;
}

export type OrderSearchResult<T> = { kind: 'empty'; orders?: undefined } | { kind: 'matches'; orders: T[] };

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// '#13' zoekt uitsluitend ordernummer 13. Een kaal getal zoekt eerst het exacte ordernummer en valt anders terug op
// klant en referentie; zo levert '1' nooit order 13 of 113 op. Productregels worden bewust niet doorzocht.
export function findSalesOrders<T extends FindableOrder>(orders: readonly T[], query: string): OrderSearchResult<T> {
    const term = normalize(query);
    if (term === '') return { kind: 'empty' };
    const number = /^#?\s*(\d{1,10})$/.exec(term);
    if (number) {
        const exact = orders.filter((order) => order.id === Number(number[1]));
        if (exact.length > 0 || term.startsWith('#')) return { kind: 'matches', orders: exact };
    }
    return {
        kind: 'matches',
        orders: orders.filter((order) => normalize(order.customerName ?? '').includes(term) || normalize(order.customerReference ?? '').includes(term)),
    };
}
