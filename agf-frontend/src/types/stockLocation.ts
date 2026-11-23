import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';

// Voorraadlocaties (feedback 22 november). Een pallet of inkooporder zonder locatie ligt in de eigen loods.
export type StockLocationKind = 'OwnWarehouse' | 'Supplier' | 'Crossdock' | 'ExternalStorage' | 'InTransit';
export const STOCK_LOCATION_KINDS: readonly StockLocationKind[] = ['OwnWarehouse', 'Supplier', 'Crossdock', 'ExternalStorage', 'InTransit'];

export interface StockLocation {
    id: number;
    name: string;
    kind: StockLocationKind;
    address: string | null;
    countryCode: string | null;
    isActive: boolean;
    inUse: boolean;
    version: number;
}

export interface SaveStockLocation {
    name: string;
    kind: StockLocationKind;
    address: string | null;
    countryCode: string | null;
    isActive: boolean;
    expectedVersion?: number;
}

export const isStockLocationKind = (value: unknown): value is StockLocationKind => isString(value) && (STOCK_LOCATION_KINDS as readonly string[]).includes(value);

export const isStockLocation = (value: unknown): value is StockLocation => isObject(value) && isNumber(value.id) && isString(value.name) && isStockLocationKind(value.kind)
    && isNullableString(value.address) && isNullableString(value.countryCode) && typeof value.isActive === 'boolean' && typeof value.inUse === 'boolean' && isNumber(value.version);

export const isStockLocationList = (value: unknown): value is StockLocation[] => isArrayOf(value, isStockLocation);
