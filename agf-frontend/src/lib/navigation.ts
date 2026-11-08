// Het menu volgt het proces: Inkoop, Voorraad, Verkoop en Stamgegevens, met Start ervoor, Instellingen en Help erna en de oude
// schermen apart onder "Oud" (niet meer bijgewerkt). Op brede schermen staat het boven (TopNav: groepen als tabs, de schermen van de
// actieve groep in een gekleurde balk eronder); op smalle schermen klapt het uit onder de knop Menu (AppMenu). Alleen schermen die bestaan staan erin: geen Leveringen- of
// Verkoopfacturenpagina, want die zijn er (nog) niet. Geen imports: met node:test te toetsen.

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
            { id: 'partijen', href: '/partijen', labelKey: 'nav_partijen' },
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

// Kleuren per groep (feedback 8 november): het menu staat boven, en de kleur laat zien in welk deel van het proces je bent. De actieve
// groep is een gekleurde tab; de balk eronder met de schermen van die groep heeft dezelfde kleur, licht. Witte tekst op -700 haalt
// het contrast van 4,5:1. Volledige klassenamen, zodat Tailwind ze vindt.
export interface GroupTheme {
    tabActive: string;
    dot: string;
    bar: string;
    itemActive: string;
    itemIdle: string;
}

export const GROUP_THEME: Readonly<Record<NavGroup['id'], GroupTheme>> = {
    purchase: { tabActive: 'bg-blue-700 text-white', dot: 'bg-blue-600', bar: 'border-blue-200 bg-blue-50', itemActive: 'bg-white text-blue-900 ring-1 ring-blue-300', itemIdle: 'text-blue-900 hover:bg-white/70' },
    stock: { tabActive: 'bg-emerald-700 text-white', dot: 'bg-emerald-600', bar: 'border-emerald-200 bg-emerald-50', itemActive: 'bg-white text-emerald-900 ring-1 ring-emerald-300', itemIdle: 'text-emerald-900 hover:bg-white/70' },
    sales: { tabActive: 'bg-orange-700 text-white', dot: 'bg-orange-600', bar: 'border-orange-200 bg-orange-50', itemActive: 'bg-white text-orange-900 ring-1 ring-orange-300', itemIdle: 'text-orange-900 hover:bg-white/70' },
    master: { tabActive: 'bg-purple-700 text-white', dot: 'bg-purple-600', bar: 'border-purple-200 bg-purple-50', itemActive: 'bg-white text-purple-900 ring-1 ring-purple-300', itemIdle: 'text-purple-900 hover:bg-white/70' },
    legacy: { tabActive: 'bg-gray-600 text-white', dot: 'bg-gray-500', bar: 'border-gray-300 bg-gray-100', itemActive: 'bg-white text-gray-900 ring-1 ring-gray-300', itemIdle: 'text-gray-700 hover:bg-white/70' },
};

// Een groep aanklikken opent haar eerste scherm; de balk eronder toont dan de andere schermen van die groep.
export function groupHome(group: NavGroup): string {
    return group.items[0].href;
}

// De groep van deze pagina (ook "Oud"), of null op Start, Instellingen, Help en pagina's buiten het menu.
export function activeGroup(pathname: string): NavGroup | null {
    const groupId = activeNav(pathname)?.groupId ?? null;
    if (groupId === null) return null;
    return groupId === 'legacy' ? LEGACY_GROUP : NAV_GROUPS.find((group) => group.id === groupId) ?? null;
}

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
