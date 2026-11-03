// Het menu volgt het proces: Inkoop, Voorraad, Verkoop en Stamgegevens, met Start bovenaan, Instellingen en Help eronder en de oude
// schermen onderaan onder "Oud" (dichtgeklapt, niet meer bijgewerkt). Alleen schermen die bestaan staan erin: geen Partijen-,
// Leveringen- of Verkoopfacturenpagina, want die zijn er (nog) niet. Geen imports: met node:test te toetsen.

export interface NavItem {
    id: string;
    href: string;
    labelKey: string;
    // Extra paden die bij dit item horen (een detailroute of een oude route die doorverwijst).
    alsoActiveOn?: readonly string[];
    // Paden die niet bij dit item horen, ook al beginnen ze ermee (/purchase-orders/new hoort bij "Inkooporder aanmaken").
    notActiveOn?: readonly string[];
}

export interface NavGroup {
    id: 'purchase' | 'stock' | 'sales' | 'master' | 'legacy';
    labelKey: string;
    items: readonly NavItem[];
}

export const START_ITEM: NavItem = { id: 'start', href: '/dashboard', labelKey: 'nav_start' };

export const NAV_GROUPS: readonly NavGroup[] = [
    {
        id: 'purchase', labelKey: 'nav_group_purchase', items: [
            { id: 'purchase-orders', href: '/purchase-orders', labelKey: 'nav_purchase_orders', notActiveOn: ['/purchase-orders/new'] },
            { id: 'purchase-order-new', href: '/purchase-orders/new', labelKey: 'nav_purchase_order_new' },
            { id: 'receipts', href: '/receipts', labelKey: 'nav_receipts' },
            { id: 'inspection', href: '/inspection', labelKey: 'nav_inspection' },
            { id: 'purchase-invoices', href: '/purchase-invoices', labelKey: 'nav_purchase_invoices' },
        ],
    },
    {
        id: 'stock', labelKey: 'nav_group_stock', items: [
            { id: 'availability', href: '/availability', labelKey: 'nav_availability' },
        ],
    },
    {
        id: 'sales', labelKey: 'nav_group_sales', items: [
            { id: 'sales-orders', href: '/sales-orders', labelKey: 'nav_sales_orders' },
            { id: 'direct-trade', href: '/direct-trade', labelKey: 'nav_direct_trade' },
        ],
    },
    {
        id: 'master', labelKey: 'nav_group_master', items: [
            { id: 'relations', href: '/relations', labelKey: 'nav_relations' },
            { id: 'articles', href: '/articles', labelKey: 'nav_articles' },
        ],
    },
];

export const SETTINGS_ITEMS: readonly NavItem[] = [
    { id: 'settings', href: '/settings', labelKey: 'nav_settings' },
    { id: 'help', href: '/help', labelKey: 'nav_help' },
];

// De oude flows: blijven bestaan en bereikbaar, maar staan apart en worden niet meer bijgewerkt.
export const LEGACY_GROUP: NavGroup = {
    id: 'legacy', labelKey: 'nav_group_legacy', items: [
        { id: 'orders', href: '/orders', labelKey: 'orders_legacy_nav' },
        { id: 'inventory', href: '/inventory', labelKey: 'inventory_legacy_nav' },
        { id: 'incoming-invoices', href: '/incoming-invoices', labelKey: 'invoices_legacy_nav' },
        { id: 'invoices', href: '/invoices', labelKey: 'nav_legacy_sales_invoices' },
        { id: 'products', href: '/products', labelKey: 'nav_legacy_products' },
        { id: 'reports', href: '/reports', labelKey: 'nav_legacy_reports' },
        { id: 'tracking', href: '/tracking', labelKey: 'tracking_nav' },
    ],
};

function onPath(pathname: string, href: string): boolean {
    return pathname === href || pathname.startsWith(`${href}/`);
}

export function isItemActive(item: NavItem, pathname: string): boolean {
    if ((item.notActiveOn ?? []).some((path) => onPath(pathname, path))) return false;
    return onPath(pathname, item.href) || (item.alsoActiveOn ?? []).some((path) => onPath(pathname, path));
}

// Het actieve item en zijn groep voor deze pagina (null buiten het menu).
export function activeNav(pathname: string): { itemId: string; groupId: NavGroup['id'] | null } | null {
    if (isItemActive(START_ITEM, pathname)) return { itemId: START_ITEM.id, groupId: null };
    for (const group of [...NAV_GROUPS, LEGACY_GROUP]) {
        const item = group.items.find((candidate) => isItemActive(candidate, pathname));
        if (item) return { itemId: item.id, groupId: group.id };
    }
    const setting = SETTINGS_ITEMS.find((candidate) => isItemActive(candidate, pathname));
    return setting ? { itemId: setting.id, groupId: null } : null;
}

// Oude adressen blijven werken: /supply (met #plan, #receive of zonder hash) en de oude zijbalklinks verwijzen door naar het
// scherm met de nieuwe naam. Klantgebonden aanvoer had geen eigen adres; die zat achter de keuze "Directe handel" op /supply.
export function supplyRedirect(hash: string): string {
    const value = hash.replace(/^#/, '').toLowerCase();
    if (value === 'receive') return '/receipts';
    if (value === 'direct' || value === 'directtrade') return '/direct-trade';
    return '/purchase-orders/new';
}
