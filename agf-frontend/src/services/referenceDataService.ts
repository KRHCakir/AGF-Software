import { ApiError, getJson } from './apiClient';
import type { DeliveryTerm } from '../lib/deliveryTerm';
import {
    CustomerOption,
    ProductOption,
    VendorOption,
    isNamedList,
    isProductList,
    toProductOption,
} from '../types/referenceData';

// Getypeerde loaders voor bestaande stamgegevens-endpoints (zelfde endpoints als de legacy-services,
// maar via apiConfig en zonder `any`). Alleen lezen.

const byName = <T extends { name: string }>(a: T, b: T) => a.name.localeCompare(b.name);

// De leveringsconditie van een klant of leverancier in de lijst (ontbreekt bij een oudere server): null = geen.
function deliveryTermOf(item: { [key: string]: unknown }): DeliveryTerm | null {
    const text = (value: unknown) => (typeof value === 'string' && value.trim() !== '' ? value : null);
    const kind = text(item.deliveryTermKind);
    return kind === null ? null : { deliveryTermKind: kind, incoterm: text(item.incoterm), incotermPlace: text(item.incotermPlace), deliveryTermText: text(item.deliveryTermText) };
}

export async function getVendors(signal?: AbortSignal): Promise<VendorOption[]> {
    const json = await getJson('/purchase-invoices/vendors', signal);
    if (!isNamedList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij leveranciers.');
    }
    const text = (value: unknown) => (typeof value === 'string' && value.trim() !== '' ? value : null);
    return json.map((item) => ({ id: item.id, name: item.name, searchCode: text(item.searchCode), vatNumber: text(item.vatNumber), isActive: item.isActive !== false,
        defaultCurrency: text(item.defaultCurrency), deliveryTerm: deliveryTermOf(item) })).sort(byName);
}

export async function getCustomers(signal?: AbortSignal): Promise<CustomerOption[]> {
    const json = await getJson('/customers', signal);
    if (!isNamedList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij klanten.');
    }
    const text = (value: unknown) => (typeof value === 'string' && value.trim() !== '' ? value : null);
    return json.map((item) => ({ id: item.id, name: item.name, searchCode: text(item.searchCode), isActive: item.isActive !== false, city: text(item.city), vatNumber: text(item.vatNumber),
        deliveryTerm: deliveryTermOf(item) })).sort(byName);
}

export async function getProductsWithVariants(signal?: AbortSignal): Promise<ProductOption[]> {
    const json = await getJson('/products?includeVariants=true', signal);
    if (!isProductList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij producten.');
    }
    return json.map(toProductOption).filter((product) => product.variants.length > 0).sort(byName);
}
