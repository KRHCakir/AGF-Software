import { PACKAGE_UNITS, PackageUnit } from './availability';
import { JsonObject, isArrayOf, isNumber, isObject, isOptionalNullableString, isString } from './guards';

const isPackageUnit = (value: unknown): value is PackageUnit => typeof value === 'string' && (PACKAGE_UNITS as readonly string[]).includes(value);

// Alleen de velden die de werkplek gebruikt uit de bestaande endpoints
// GET /api/purchase-invoices/vendors, GET /api/customers en GET /api/products?includeVariants=true.

export interface VendorOption {
    id: number;
    name: string;
    // Zoekcode, btw-nummer en actief-vlag van de relatie: alleen om op te zoeken in de leverancierskeuze.
    searchCode: string | null;
    vatNumber: string | null;
    isActive: boolean;
}

export interface CustomerOption {
    id: number;
    name: string;
    // Zoekcode en actief-vlag van de relatie (Relaties); inactieve klanten zijn niet kiesbaar voor een nieuwe order.
    searchCode: string | null;
    isActive: boolean;
    // Plaats van het hoofdadres en btw-nummer: alleen om op te zoeken in de klantkeuze.
    city?: string | null;
    vatNumber?: string | null;
}

// Klant aanmaken vanuit Verkoop (POST /api/customers). Lege optionele velden gaan als null mee.
export interface CreateCustomerRequest {
    requestId: string;
    name: string;
    contactPerson: string | null;
    email: string | null;
    phone: string | null;
    shippingAddress: string | null;
    acknowledgeSimilarNames: boolean;
}

export interface CreatedCustomer {
    id: number;
    name: string;
    contactPerson: string | null;
    email: string | null;
    phone: string | null;
    shippingAddress: string | null;
}

export interface SimilarCustomer {
    id: number;
    name: string;
    contactPerson: string | null;
    phone: string | null;
}

export interface VariantOption {
    id: number;
    name: string;
    origin: string | null;
    // Land van herkomst (ISO-code) als de variant er een heeft; zoeken op "PE" vindt Peru.
    originCountryCode?: string | null;
    // Handelsklasse en merk (bij ontvangst alleen gevraagd als de varianten van een artikel daarin verschillen).
    tradeClass?: string | null;
    brand?: string | null;
    // Gewichtsmethode van de variant (Weighed of Calculated): de rondleiding vult daarmee het juiste kg-veld in.
    weightMethod?: string | null;
    packaging: string | null;
    sizeCategory: string | null;
    // Bestaande snelcode van de variant (beheer via Producten); kiest alleen een variant, nooit hoeveelheid of gewicht.
    shortcutCode: string | null;
    // Standaard verpakkingen per pallet (invoerhulp bij "Pallet"-invoer; aanpasbaar per regel).
    defaultPackagesPerPallet: number | null;
    // Verpakkingseenheid uit de artikelstamgegevens: vult een lege eenheid op een nieuwe regel voor; nooit een overschrijving.
    packagingUnit: PackageUnit | null;
    // Standaard netto en bruto kg per verpakking: alleen voor de rekenregel onder het aantal.
    defaultNetKgPerPackage?: number | null;
    defaultGrossKgPerPackage?: number | null;
}

export interface ProductOption {
    id: number;
    name: string;
    variants: VariantOption[];
}

type Named = JsonObject & { id: number; name: string };

function isNamed(value: unknown): value is Named {
    return isObject(value) && isNumber(value.id) && isString(value.name);
}

function isVariant(value: unknown): value is Named {
    return isNamed(value)
        && isOptionalNullableString(value.origin)
        && isOptionalNullableString(value.packaging)
        && isOptionalNullableString(value.sizeCategory)
        && isOptionalNullableString(value.shortcutCode)
        && isOptionalNullableString(value.packagingUnit)
        && (value.defaultPackagesPerPallet === undefined || value.defaultPackagesPerPallet === null || isNumber(value.defaultPackagesPerPallet));
}

export function isCreatedCustomer(value: unknown): value is CreatedCustomer {
    return isNamed(value)
        && isOptionalNullableString(value.contactPerson)
        && isOptionalNullableString(value.email)
        && isOptionalNullableString(value.phone)
        && isOptionalNullableString(value.shippingAddress);
}

export function isSimilarCustomerList(value: unknown): value is SimilarCustomer[] {
    return isArrayOf(value, (item): item is SimilarCustomer => isNamed(item) && isOptionalNullableString(item.contactPerson) && isOptionalNullableString(item.phone));
}

export function isNamedList(value: unknown): value is Named[] {
    return isArrayOf(value, isNamed);
}

export function isProductList(value: unknown): value is Named[] {
    return isArrayOf(value, (item): item is Named =>
        isNamed(item) && (item.variants === null || item.variants === undefined || isArrayOf(item.variants, isVariant)));
}

function optionalString(value: unknown): string | null {
    return typeof value === 'string' && value.trim() !== '' ? value : null;
}

export function toVariantOption(value: Named): VariantOption {
    return {
        id: value.id,
        name: value.name,
        origin: optionalString(value.origin),
        originCountryCode: optionalString(value.originCountryCode),
        tradeClass: optionalString(value.tradeClass),
        brand: optionalString(value.brand),
        weightMethod: optionalString(value.weightMethod),
        packaging: optionalString(value.packaging),
        sizeCategory: optionalString(value.sizeCategory),
        shortcutCode: optionalString(value.shortcutCode),
        defaultPackagesPerPallet: typeof value.defaultPackagesPerPallet === 'number' ? value.defaultPackagesPerPallet : null,
        packagingUnit: isPackageUnit(value.packagingUnit) ? value.packagingUnit : null,
        defaultNetKgPerPackage: typeof value.defaultNetKgPerPackage === 'number' ? value.defaultNetKgPerPackage : null,
        defaultGrossKgPerPackage: typeof value.defaultGrossKgPerPackage === 'number' ? value.defaultGrossKgPerPackage : null,
    };
}

export function toProductOption(value: Named): ProductOption {
    const variants = Array.isArray(value.variants) ? (value.variants as Named[]).map(toVariantOption) : [];
    return {
        id: value.id,
        name: value.name,
        variants: variants.sort((a, b) => a.name.localeCompare(b.name)),
    };
}
