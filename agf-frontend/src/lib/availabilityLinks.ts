import { PACKAGE_UNITS, PackageUnit, SupplyRoute } from '../types/availability';
import { AvailabilityOverviewQuery } from '../services/availabilityService';

// Contextgerichte links naar /availability. De filters zijn echte API-filters; `focus` markeert alleen een kolom.

export type AvailabilityFocus = 'expected' | 'quarantine' | 'inTransit' | 'available';

const FOCUS_VALUES: readonly AvailabilityFocus[] = ['expected', 'quarantine', 'inTransit', 'available'];

export interface AvailabilityLink {
    route: SupplyRoute;
    focus: AvailabilityFocus;
    productVariantId?: number;
    customerId?: number;
}

export function availabilityHref(link: AvailabilityLink): string {
    const params = new URLSearchParams({ route: link.route });
    if (link.productVariantId !== undefined) {
        params.set('productVariantId', String(link.productVariantId));
    }
    if (link.route === 'DirectTrade' && link.customerId !== undefined) {
        params.set('customerId', String(link.customerId));
    }
    params.set('focus', link.focus);
    return `/availability?${params.toString()}`;
}

// Eén variant in de context: dan filteren we daarop; bij meerdere varianten alleen op route.
export function singleVariantId(variantIds: number[]): number | undefined {
    const unique = Array.from(new Set(variantIds));
    return unique.length === 1 ? unique[0] : undefined;
}

export interface ParsedAvailabilityParams {
    query: AvailabilityOverviewQuery;
    focus: AvailabilityFocus | null;
    invalid: string[];
}

function positiveInt(value: string): number | null {
    return /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
}

// Strikt: een ongeldige waarde wordt genegeerd en gemeld, nooit "ongeveer" toegepast.
export function parseAvailabilityParams(params: { get(name: string): string | null }): ParsedAvailabilityParams {
    const query: AvailabilityOverviewQuery = {};
    const invalid: string[] = [];

    const route = params.get('route');
    if (route !== null) {
        if (route === 'Warehouse' || route === 'DirectTrade') {
            query.route = route;
        } else {
            invalid.push('route');
        }
    }

    const variant = params.get('productVariantId');
    if (variant !== null) {
        const id = positiveInt(variant);
        if (id === null) {
            invalid.push('productVariantId');
        } else {
            query.productVariantId = id;
        }
    }

    const customer = params.get('customerId');
    if (customer !== null) {
        const id = positiveInt(customer);
        if (id === null || query.route !== 'DirectTrade') {
            invalid.push('customerId');
        } else {
            query.customerId = id;
        }
    }

    const unit = params.get('unit');
    if (unit !== null) {
        if ((PACKAGE_UNITS as readonly string[]).includes(unit)) {
            query.unit = unit as PackageUnit;
        } else {
            invalid.push('unit');
        }
    }

    const focusParam = params.get('focus');
    let focus: AvailabilityFocus | null = null;
    if (focusParam !== null) {
        if ((FOCUS_VALUES as readonly string[]).includes(focusParam)) {
            focus = focusParam as AvailabilityFocus;
        } else {
            invalid.push('focus');
        }
    }

    return { query, focus, invalid };
}
