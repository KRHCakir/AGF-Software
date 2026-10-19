import {
    AvailabilityCategory,
    AvailabilityQuantity,
    DirectTradeSupply,
    LocationContext,
    LogisticsStatus,
    PACKAGE_UNITS,
    PackageUnit,
    QualityBreakdown,
    QualityStatus,
    VariantAvailability,
    WarehouseSupply,
} from '../types/availability';

// Pure hulpfuncties voor de beschikbaarheidspagina. Geen conversies tussen eenheden en geen geschatte gewichten:
// alleen optellen binnen één categorie van één variant + eenheid, voor weergave.

export type UnitFilter = 'all' | 'unknown' | PackageUnit;

export interface WarehouseRow {
    key: string;
    variant: VariantAvailability;
    supply: WarehouseSupply;
}

export interface DirectTradeRow {
    key: string;
    variant: VariantAvailability;
    supply: DirectTradeSupply;
}

export function breakdownTotal(breakdown: QualityBreakdown): AvailabilityQuantity {
    return {
        quantity: breakdown.approved.quantity + breakdown.quarantine.quantity + breakdown.blocked.quantity + breakdown.rejected.quantity,
        netWeightKg: breakdown.approved.netWeightKg + breakdown.quarantine.netWeightKg + breakdown.blocked.netWeightKg + breakdown.rejected.netWeightKg,
    };
}

export function normalizeSearch(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

export function matchesSearch(variant: VariantAvailability, search: string): boolean {
    const term = normalizeSearch(search);
    if (term === '') {
        return true;
    }
    return normalizeSearch(`${variant.productName} ${variant.variantName}`).includes(term);
}

export function matchesUnit(unit: PackageUnit | null, filter: UnitFilter): boolean {
    if (filter === 'all') {
        return true;
    }
    if (filter === 'unknown') {
        return unit === null;
    }
    return unit === filter;
}

function unitOrder(unit: PackageUnit | null): number {
    return unit === null ? PACKAGE_UNITS.length : PACKAGE_UNITS.indexOf(unit);
}

// Eén rij per variant + eenheid; eenheden worden nooit samengevoegd.
export function warehouseRows(variants: VariantAvailability[], search: string, unit: UnitFilter): WarehouseRow[] {
    return variants
        .filter((variant) => matchesSearch(variant, search))
        .flatMap((variant) => variant.warehouseSupply
            .filter((supply) => matchesUnit(supply.unit, unit))
            .sort((a, b) => unitOrder(a.unit) - unitOrder(b.unit))
            .map((supply) => ({ key: `${variant.productVariantId}:${supply.unit ?? 'unknown'}`, variant, supply })));
}

// Eén rij per klant + variant + eenheid; gesorteerd op klant zodat de klantbinding vooraan staat.
export function directTradeRows(variants: VariantAvailability[], search: string, unit: UnitFilter): DirectTradeRow[] {
    return variants
        .filter((variant) => matchesSearch(variant, search))
        .flatMap((variant) => variant.directTradeSupply
            .filter((supply) => matchesUnit(supply.unit, unit))
            .map((supply) => ({
                key: `${variant.productVariantId}:${supply.customerId ?? 'unknown'}:${supply.unit ?? 'unknown'}`,
                variant,
                supply,
            })))
        .sort((a, b) =>
            (a.supply.customerName ?? '').localeCompare(b.supply.customerName ?? '')
            || (a.supply.customerId ?? 0) - (b.supply.customerId ?? 0)
            || a.variant.productName.localeCompare(b.variant.productName)
            || a.variant.variantName.localeCompare(b.variant.variantName)
            || unitOrder(a.supply.unit) - unitOrder(b.supply.unit));
}

export function numberLocale(locale: string): string {
    return locale === 'nl' ? 'nl-NL' : 'en-GB';
}

export function formatQuantity(value: number, locale: string): string {
    return new Intl.NumberFormat(numberLocale(locale), { maximumFractionDigits: 3 }).format(value);
}

export function formatKg(value: number, locale: string): string {
    return `${new Intl.NumberFormat(numberLocale(locale), { maximumFractionDigits: 3 }).format(value)} kg`;
}

export function formatDateTime(value: string, locale: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? value
        : new Intl.DateTimeFormat(numberLocale(locale), { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export function formatDate(value: string, locale: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? value
        : new Intl.DateTimeFormat(numberLocale(locale), { dateStyle: 'medium' }).format(date);
}

export function formatTime(value: string, locale: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? value
        : new Intl.DateTimeFormat(numberLocale(locale), { timeStyle: 'short' }).format(date);
}

// i18n-sleutels per waarde; onbekende waarden (bijvoorbeeld een nieuwe backendstatus) tonen we ongewijzigd.
const CATEGORY_KEYS: Record<AvailabilityCategory, string> = {
    warehouse: 'availability_category_warehouse',
    receivedOutsideWarehouse: 'availability_category_outside',
    inTransit: 'availability_category_in_transit',
    expectedOnPallets: 'availability_category_expected_on_pallets',
    closed: 'availability_category_closed',
};

const LOGISTICS_KEYS: Record<LogisticsStatus, string> = {
    Expected: 'availability_logistics_expected',
    InTransit: 'availability_logistics_in_transit',
    Received: 'availability_logistics_received',
    Dispatched: 'availability_logistics_dispatched',
    Delivered: 'availability_logistics_delivered',
    Cancelled: 'availability_logistics_cancelled',
};

const LOCATION_KEYS: Record<LocationContext, string> = {
    Supplier: 'availability_location_supplier',
    Warehouse: 'availability_location_warehouse',
    Crossdock: 'availability_location_crossdock',
    InTransit: 'availability_location_in_transit',
    Customer: 'availability_location_customer',
    External: 'availability_location_external',
};

const COMMITMENT_STATUS_KEYS: Record<string, string> = {
    Expected: 'availability_commitment_expected',
    PartiallyReceived: 'availability_commitment_partially_received',
    Received: 'availability_commitment_received',
    Cancelled: 'availability_commitment_cancelled',
    Delivered: 'availability_commitment_delivered',
};

export function categoryKey(category: string): string | null {
    return category in CATEGORY_KEYS ? CATEGORY_KEYS[category as AvailabilityCategory] : null;
}

export function logisticsKey(status: string): string | null {
    return status in LOGISTICS_KEYS ? LOGISTICS_KEYS[status as LogisticsStatus] : null;
}

export function locationKey(location: string): string | null {
    return location in LOCATION_KEYS ? LOCATION_KEYS[location as LocationContext] : null;
}

export function commitmentStatusKey(status: string): string | null {
    return COMMITMENT_STATUS_KEYS[status] ?? null;
}

export interface QualityStyle {
    key: string;
    icon: string;
    className: string;
}

// Kleur nooit als enige drager: altijd icoon + tekst, met AA-contrast (-800 op -100).
const QUALITY_STYLES: Record<QualityStatus, QualityStyle> = {
    Approved: { key: 'availability_quality_approved', icon: '✓', className: 'bg-green-100 text-green-800 border-green-300' },
    Quarantine: { key: 'availability_quality_quarantine', icon: '⏳', className: 'bg-amber-100 text-amber-900 border-amber-300' },
    Blocked: { key: 'availability_quality_blocked', icon: '⛔', className: 'bg-red-100 text-red-800 border-red-300' },
    Rejected: { key: 'availability_quality_rejected', icon: '✕', className: 'bg-gray-200 text-gray-800 border-gray-400' },
};

export function qualityStyle(status: string): QualityStyle | null {
    return status in QUALITY_STYLES ? QUALITY_STYLES[status as QualityStatus] : null;
}
