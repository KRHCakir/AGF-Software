import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Transport op een verkooporder (documenten deel 2). Backend: AgfAdmin/DTOs/SalesOrderTransportDtos.cs. Leeg = de standaard die de server
// invult (default*): laadplaats de loods, losplaats het afleveradres van de klant, losreferentie de klantreferentie, soort pallet de standaard.
export type TransportMode = 'Carrier' | 'Own';

export interface TransportAddressOption { id: number; purpose: string; text: string | null; isDefault: boolean }

export interface SalesOrderTransport {
    salesOrderId: number;
    exists: boolean;
    mode: TransportMode | null;
    carrierRelationId: number | null;
    carrierName: string | null;
    carrierAddress: string | null;
    loadingPlace: string | null;
    defaultLoadingPlace: string | null;
    unloadingAddressId: number | null;
    unloadingPlace: string | null;
    defaultUnloadingPlace: string | null;
    unloadingAddresses: TransportAddressOption[];
    loadAt: string | null;
    unloadDate: string | null;
    temperatureC: number | null;
    loadingReference: string | null;
    unloadingReference: string | null;
    defaultUnloadingReference: string | null;
    remarks: string | null;
    palletKind: string | null;
    defaultPalletKind: string | null;
    version: number;
    hasTransportOrder: boolean;
}

export interface SaveSalesOrderTransport {
    expectedVersion: number | null;
    // loadAt leeg laat de laadtijd van de order staan; clearLoadAt wist haar (de gebruiker maakte de laaddatum leeg).
    clearLoadAt: boolean;
    mode: TransportMode;
    carrierRelationId: number | null;
    loadingPlace: string | null;
    unloadingAddressId: number | null;
    unloadingPlace: string | null;
    loadAt: string | null;
    unloadDate: string | null;
    temperatureC: number | null;
    loadingReference: string | null;
    unloadingReference: string | null;
    remarks: string | null;
    palletKind: string | null;
}


const isAddress = (value: unknown): value is TransportAddressOption => isObject(value) && isNumber(value.id) && isString(value.purpose) && isNullableString(value.text);

export function isSalesOrderTransport(value: unknown): value is SalesOrderTransport {
    return isObject(value) && isNumber(value.salesOrderId) && typeof value.exists === 'boolean' && (value.mode === null || value.mode === 'Carrier' || value.mode === 'Own')
        && isNullableNumber(value.carrierRelationId) && isNullableString(value.carrierName) && isNullableString(value.loadingPlace) && isNullableString(value.defaultLoadingPlace)
        && isNullableNumber(value.unloadingAddressId) && isNullableString(value.unloadingPlace) && isNullableString(value.defaultUnloadingPlace)
        && isArrayOf(value.unloadingAddresses, isAddress) && isNullableString(value.loadAt) && isNullableString(value.unloadDate) && isNullableNumber(value.temperatureC)
        && isNullableString(value.loadingReference) && isNullableString(value.unloadingReference) && isNullableString(value.remarks) && isNullableString(value.palletKind)
        && isNumber(value.version) && typeof value.hasTransportOrder === 'boolean';
}
