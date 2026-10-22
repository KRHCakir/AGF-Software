import { isWeightMethod, type WeightMethod } from '../lib/weight';
import { PACKAGE_UNITS, PackageUnit, SupplyRoute } from './availability';
import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Exact het contract van POST/GET /api/supply-commitments (backend: AgfAdmin/DTOs/SupplyCommitmentDtos.cs).

export interface CreateSupplyCommitmentLineRequest {
    productVariantId: number;
    expectedQuantity: number;
    expectedUnit: PackageUnit;
    expectedNetWeightKg: number | null;
    lotNumber: string | null;
    // Optioneel, beide of geen: expectedQuantity = palletCount × packagesPerPallet (de server controleert dit).
    palletCount: number | null;
    packagesPerPallet: number | null;
}

export interface CreateSupplyCommitmentRequest {
    requestId: string;
    vendorId: number;
    customerId: number | null;
    route: SupplyRoute;
    origin: string;
    destination: string | null;
    expectedAt: string;
    lines: CreateSupplyCommitmentLineRequest[];
}

export interface SupplyCommitmentLine {
    id: number;
    productVariantId: number;
    productName: string | null;
    productVariantName: string | null;
    lotNumber: string | null;
    expectedQuantity: number;
    expectedUnit: PackageUnit | null;
    expectedNetWeightKg: number | null;
    // Gewichtsmethode en standaard kg per verpakking van de variant: invoerhulp bij ontvangst, per regel aanpasbaar.
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: number | null;
    // Oorspronkelijke palletinvoer (null bij gewone invoer) en de standaard van de variant als invoerhulp.
    palletCount: number | null;
    packagesPerPallet: number | null;
    defaultPackagesPerPallet: number | null;
    // Standaard-GGN van het artikel: voorstel voor de inhoudsregel bij ontvangst; per regel aanpasbaar of leeg te maken.
    defaultGgn: string | null;
}

export interface SupplyCommitment {
    id: number;
    requestId: string;
    vendorId: number;
    vendorName: string | null;
    customerId: number | null;
    customerName: string | null;
    route: SupplyRoute;
    status: string;
    origin: string;
    destination: string | null;
    expectedAt: string | null;
    lines: SupplyCommitmentLine[];
}

function isRoute(value: unknown): value is SupplyRoute {
    return value === 'Warehouse' || value === 'DirectTrade';
}

function isNullableUnit(value: unknown): value is PackageUnit | null {
    return value === null || (isString(value) && (PACKAGE_UNITS as readonly string[]).includes(value));
}

function isLine(value: unknown): value is SupplyCommitmentLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.productVariantId)
        && isNullableString(value.productName)
        && isNullableString(value.productVariantName)
        && isNullableString(value.lotNumber)
        && isNumber(value.expectedQuantity)
        && isNullableUnit(value.expectedUnit)
        && isNullableNumber(value.expectedNetWeightKg)
        && isWeightMethod(value.weightMethod)
        && isNullableNumber(value.defaultNetKgPerPackage)
        && isNullableNumber(value.palletCount)
        && isNullableNumber(value.packagesPerPallet)
        && isNullableNumber(value.defaultPackagesPerPallet)
        && isNullableString(value.defaultGgn);
}

export function isSupplyCommitment(value: unknown): value is SupplyCommitment {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.requestId)
        && isNumber(value.vendorId)
        && isNullableString(value.vendorName)
        && isNullableNumber(value.customerId)
        && isNullableString(value.customerName)
        && isRoute(value.route)
        && isString(value.status)
        && isString(value.origin)
        && isNullableString(value.destination)
        && isNullableString(value.expectedAt)
        && isArrayOf(value.lines, isLine);
}

export function isSupplyCommitmentList(value: unknown): value is SupplyCommitment[] {
    return isArrayOf(value, isSupplyCommitment);
}
