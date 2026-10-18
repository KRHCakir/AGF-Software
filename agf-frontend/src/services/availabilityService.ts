import { ApiError, getJson } from './apiClient';
import {
    AvailabilityDrillDown,
    AvailabilityOverview,
    PackageUnit,
    SupplyRoute,
    isAvailabilityDrillDown,
    isAvailabilityOverview,
} from '../types/availability';

// Alleen-lezen client voor de Availability API. Geen schrijfacties.
// De fouttypen komen uit de gedeelde apiClient; de oude namen blijven beschikbaar voor bestaande componenten.
export { ApiError as AvailabilityApiError, toApiError as toAvailabilityError } from './apiClient';
export type { ApiErrorKind as AvailabilityErrorKind } from './apiClient';

// Echte API-filters van GET /api/availability (gebruikt door URL-filters op de pagina).
export interface AvailabilityOverviewQuery {
    productVariantId?: number;
    route?: SupplyRoute;
    customerId?: number;
    unit?: PackageUnit;
}

export async function getAvailabilityOverview(query: AvailabilityOverviewQuery = {}, signal?: AbortSignal): Promise<AvailabilityOverview> {
    const params = new URLSearchParams();
    if (query.productVariantId !== undefined) {
        params.set('productVariantId', String(query.productVariantId));
    }
    if (query.route !== undefined) {
        params.set('route', query.route);
    }
    if (query.customerId !== undefined) {
        params.set('customerId', String(query.customerId));
    }
    if (query.unit !== undefined) {
        params.set('unit', query.unit);
    }
    const suffix = params.toString() === '' ? '' : `?${params.toString()}`;

    const json = await getJson(`/availability${suffix}`, signal);
    if (!isAvailabilityOverview(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord van de server.');
    }
    return json;
}

export interface VariantDrillDownQuery {
    route: SupplyRoute;
    unit: PackageUnit | null;
    customerId: number | null;
    includeClosed: boolean;
}

// Een bekende eenheid gaat als filter mee naar de API. Alleen bij een onbekende eenheid (unit: null, legacy-bron)
// laat de service de parameter weg en filtert daarna exact op unit === null.
export async function getAvailabilityVariant(
    productVariantId: number,
    query: VariantDrillDownQuery,
    signal?: AbortSignal,
): Promise<AvailabilityDrillDown> {
    const params = new URLSearchParams({ route: query.route });
    if (query.unit !== null) {
        params.set('unit', query.unit);
    }
    if (query.route === 'DirectTrade' && query.customerId !== null) {
        params.set('customerId', String(query.customerId));
    }
    if (query.includeClosed) {
        params.set('includeClosed', 'true');
    }

    const json = await getJson(`/availability/variants/${productVariantId}?${params.toString()}`, signal);
    if (!isAvailabilityDrillDown(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord van de server.');
    }

    if (query.unit !== null) {
        return json;
    }
    return {
        ...json,
        sourceLines: json.sourceLines.filter((line) => line.unit === null),
        // Palletinhoud heeft altijd een expliciete eenheid en hoort dus nooit bij een rij zonder eenheid.
        contentLines: [],
    };
}
