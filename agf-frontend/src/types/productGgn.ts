import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';

// Certificaten deel 2: het voorgestelde GGN van het product per inkoopregel (GET api/purchase-orders/ggn-proposals; backend:
// ProductGgnProposalsDto). None = leeg laten, One = voorvullen, Many = laten kiezen.
export type GgnProposalKind = 'None' | 'One' | 'Many';
export interface GgnProposalOption { ggn: string; certificateId: number; validUntil: string | null; producerName: string | null }
export interface GgnProposal { productId: number; kind: GgnProposalKind; options: GgnProposalOption[] }
export interface GgnProposals { vendorId: number; date: string; proposals: GgnProposal[] }

const isKind = (value: unknown): value is GgnProposalKind => value === 'None' || value === 'One' || value === 'Many';
const isOption = (value: unknown): value is GgnProposalOption => isObject(value) && isString(value.ggn) && isNumber(value.certificateId)
    && isNullableString(value.validUntil) && isNullableString(value.producerName);
const isProposal = (value: unknown): value is GgnProposal => isObject(value) && isNumber(value.productId) && isKind(value.kind) && isArrayOf(value.options, isOption);

export function isGgnProposals(value: unknown): value is GgnProposals {
    return isObject(value) && isNumber(value.vendorId) && isString(value.date) && isArrayOf(value.proposals, isProposal);
}
