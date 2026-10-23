import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Contract van api/relations (backend: AgfAdmin/DTOs/RelationDtos.cs). Eén onderneming die klant, leverancier of beide is;
// rollen houden hun bestaande Customer-/Vendor-ID's.

export const ADDRESS_PURPOSES = ['Main', 'Billing', 'Delivery'] as const;
export type AddressPurpose = (typeof ADDRESS_PURPOSES)[number];
export const CHANNEL_KINDS = ['Phone', 'Email'] as const;
export type ChannelKind = (typeof CHANNEL_KINDS)[number];
export type RelationRoleFilter = 'all' | 'customer' | 'vendor';

export interface RelationListItem {
    id: number;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    customerId: number | null;
    vendorId: number | null;
    isActive: boolean;
    city: string | null;
    countryCode: string | null;
    email: string | null;
    phone: string | null;
    primaryContactName: string | null;
    vatNumber: string | null;
    version: number;
}

export interface RelationList {
    items: RelationListItem[];
    total: number;
    page: number;
    pageSize: number;
}

export interface RelationAddress {
    id: number | null;
    purpose: AddressPurpose;
    label: string | null;
    line1: string | null;
    line2: string | null;
    postalCode: string | null;
    city: string | null;
    region: string | null;
    countryCode: string | null;
    isDefault: boolean;
    isActive: boolean;
}

export interface ContactChannel {
    id: number | null;
    kind: ChannelKind;
    label: string | null;
    value: string;
}

export interface RelationContact {
    id: number | null;
    name: string;
    department: string | null;
    jobTitle: string | null;
    isPrimary: boolean;
    isActive: boolean;
    note: string | null;
    channels: ContactChannel[];
}

export interface VendorGgn {
    id: number | null;
    ggn: string;
    producerName: string | null;
    isActive: boolean;
}

export interface CustomerRole { customerId: number; paymentTermsDays: number }
export interface VendorRole { vendorId: number; paymentTermsDays: number; defaultCurrency: string | null; ggns: VendorGgn[] }

export interface RelationUsage {
    salesOrders: number;
    legacyOrders: number;
    directTradeCommitments: number;
    purchaseInvoices: number;
    supplyCommitments: number;
    defaultsInUse: string[];
}

export interface Relation {
    id: number;
    requestId: string | null;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    preferredLanguage: string | null;
    website: string | null;
    email: string | null;
    phone: string | null;
    vatNumber: string | null;
    vatVerificationStatus: string;
    vatVerifiedNumber: string | null;
    vatVerifiedAt: string | null;
    registrationNumber: string | null;
    registrationCountry: string | null;
    registrationType: string | null;
    legalForm: string | null;
    internalNote: string | null;
    isActive: boolean;
    createdAt: string;
    updatedAt: string;
    version: number;
    customer: CustomerRole | null;
    vendor: VendorRole | null;
    addresses: RelationAddress[];
    contacts: RelationContact[];
    usage: RelationUsage;
}

// POST (requestId) en PUT (expectedVersion): de kaart wordt volledig meegestuurd; null voor een rol = rol niet aanwezig/niet wijzigen.
export interface SaveRelationRequest {
    requestId?: string;
    expectedVersion?: number;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    preferredLanguage: string | null;
    website: string | null;
    email: string | null;
    phone: string | null;
    vatNumber: string | null;
    registrationNumber: string | null;
    registrationCountry: string | null;
    registrationType: string | null;
    legalForm: string | null;
    internalNote: string | null;
    isActive: boolean;
    acknowledgeSimilarNames: boolean;
    customer: { paymentTermsDays: number } | null;
    vendor: { paymentTermsDays: number; defaultCurrency: string; ggns: VendorGgn[] } | null;
    addresses: RelationAddress[];
    contacts: RelationContact[];
}

export interface LegacyArticleGgn { productVariantId: number; description: string; ggn: string }

export interface RelationReference {
    languages: string[];
    addressPurposes: string[];
    departmentSuggestions: string[];
    registrationTypeSuggestions: string[];
    legalFormSuggestions: string[];
    currencies: string[];
    vatVerificationStatuses: string[];
    globalGapDatabaseUrl: string;
    legacyArticleGgns: LegacyArticleGgn[];
}

export interface SimilarRelation { id: number; searchCode: string | null; name: string; customerId: number | null; vendorId: number | null }

const isPurpose = (value: unknown): value is AddressPurpose => isString(value) && (ADDRESS_PURPOSES as readonly string[]).includes(value);
const isKind = (value: unknown): value is ChannelKind => isString(value) && (CHANNEL_KINDS as readonly string[]).includes(value);

function isAddress(value: unknown): value is RelationAddress {
    return isObject(value) && isNullableNumber(value.id) && isPurpose(value.purpose) && isNullableString(value.label) && isNullableString(value.line1) && isNullableString(value.line2)
        && isNullableString(value.postalCode) && isNullableString(value.city) && isNullableString(value.region) && isNullableString(value.countryCode)
        && typeof value.isDefault === 'boolean' && typeof value.isActive === 'boolean';
}

function isChannel(value: unknown): value is ContactChannel {
    return isObject(value) && isNullableNumber(value.id) && isKind(value.kind) && isNullableString(value.label) && isString(value.value);
}

function isContact(value: unknown): value is RelationContact {
    return isObject(value) && isNullableNumber(value.id) && isString(value.name) && isNullableString(value.department) && isNullableString(value.jobTitle)
        && typeof value.isPrimary === 'boolean' && typeof value.isActive === 'boolean' && isNullableString(value.note) && isArrayOf(value.channels, isChannel);
}

function isGgn(value: unknown): value is VendorGgn {
    return isObject(value) && isNullableNumber(value.id) && isString(value.ggn) && isNullableString(value.producerName) && typeof value.isActive === 'boolean';
}

function isUsage(value: unknown): value is RelationUsage {
    return isObject(value) && isNumber(value.salesOrders) && isNumber(value.legacyOrders) && isNumber(value.directTradeCommitments) && isNumber(value.purchaseInvoices)
        && isNumber(value.supplyCommitments) && isArrayOf(value.defaultsInUse, isString);
}

export function isRelation(value: unknown): value is Relation {
    return isObject(value) && isNumber(value.id) && isNullableString(value.requestId) && isNullableString(value.searchCode) && isString(value.name) && isNullableString(value.tradeName)
        && isNullableString(value.preferredLanguage) && isNullableString(value.website) && isNullableString(value.email) && isNullableString(value.phone) && isNullableString(value.vatNumber)
        && isString(value.vatVerificationStatus) && isNullableString(value.vatVerifiedNumber) && isNullableString(value.vatVerifiedAt)
        && isNullableString(value.registrationNumber) && isNullableString(value.registrationCountry) && isNullableString(value.registrationType) && isNullableString(value.legalForm)
        && isNullableString(value.internalNote) && typeof value.isActive === 'boolean' && isString(value.createdAt) && isString(value.updatedAt) && isNumber(value.version)
        && (value.customer === null || (isObject(value.customer) && isNumber(value.customer.customerId) && isNumber(value.customer.paymentTermsDays)))
        && (value.vendor === null || (isObject(value.vendor) && isNumber(value.vendor.vendorId) && isNumber(value.vendor.paymentTermsDays) && isNullableString(value.vendor.defaultCurrency) && isArrayOf(value.vendor.ggns, isGgn)))
        && isArrayOf(value.addresses, isAddress) && isArrayOf(value.contacts, isContact) && isUsage(value.usage);
}

function isListItem(value: unknown): value is RelationListItem {
    return isObject(value) && isNumber(value.id) && isNullableString(value.searchCode) && isString(value.name) && isNullableString(value.tradeName) && isNullableNumber(value.customerId)
        && isNullableNumber(value.vendorId) && typeof value.isActive === 'boolean' && isNullableString(value.city) && isNullableString(value.countryCode) && isNullableString(value.email)
        && isNullableString(value.phone) && isNullableString(value.primaryContactName) && isNullableString(value.vatNumber) && isNumber(value.version);
}

export function isRelationList(value: unknown): value is RelationList {
    return isObject(value) && isArrayOf(value.items, isListItem) && isNumber(value.total) && isNumber(value.page) && isNumber(value.pageSize);
}

export function isRelationReference(value: unknown): value is RelationReference {
    return isObject(value) && isArrayOf(value.languages, isString) && isArrayOf(value.addressPurposes, isString) && isArrayOf(value.departmentSuggestions, isString)
        && isArrayOf(value.registrationTypeSuggestions, isString) && isArrayOf(value.legalFormSuggestions, isString) && isArrayOf(value.currencies, isString)
        && isArrayOf(value.vatVerificationStatuses, isString) && isString(value.globalGapDatabaseUrl)
        && isArrayOf(value.legacyArticleGgns, (item): item is LegacyArticleGgn => isObject(item) && isNumber(item.productVariantId) && isString(item.description) && isString(item.ggn));
}

export function isSimilarRelationList(value: unknown): value is SimilarRelation[] {
    return isArrayOf(value, (item): item is SimilarRelation => isObject(item) && isNumber(item.id) && isNullableString(item.searchCode) && isString(item.name) && isNullableNumber(item.customerId) && isNullableNumber(item.vendorId));
}
