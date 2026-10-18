import { JsonObject, isArrayOf, isNumber, isObject, isOptionalNullableString, isString } from './guards';

// Alleen de velden die de werkplek gebruikt uit de bestaande endpoints
// GET /api/incominginvoices/vendors, GET /api/customers en GET /api/products?includeVariants=true.

export interface VendorOption {
    id: number;
    name: string;
}

export interface CustomerOption {
    id: number;
    name: string;
}

export interface VariantOption {
    id: number;
    name: string;
    origin: string | null;
    packaging: string | null;
    sizeCategory: string | null;
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
        && isOptionalNullableString(value.sizeCategory);
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
        packaging: optionalString(value.packaging),
        sizeCategory: optionalString(value.sizeCategory),
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
