// Afkeuren van ontvangen inkoop (spiegel van AgfAdmin/Services/PurchaseOrderRules.Rejection.cs). Pure module voor node:test.
import { calculatedTotalKg, type WeightMethod } from './weight.ts';
import type { RejectionDecision, RejectionReason } from './purchaseOrderRules.ts';

export const REJECTION_REASONS: readonly RejectionReason[] = ['Quality', 'Ripeness', 'Temperature', 'Damaged', 'WrongArticle', 'Other'];
export const REJECTION_DECISIONS: readonly RejectionDecision[] = ['ReturnToSupplier', 'ClaimDiscount', 'Dispose'];
export const MAX_EXPLANATION_LENGTH = 500;

// Standaard-kg bij afkeuren: de hele voorraad = exact de resterende kg; berekend = aantal × kg per verpakking; gewogen = evenredig.
export function defaultRejectedKg(quantity: number, onHandQuantity: number, onHandNetWeightKg: number, weightMethod: WeightMethod | string, netKgPerPackage: number | null): number {
    if (quantity >= onHandQuantity) return onHandNetWeightKg;
    if (weightMethod === 'Calculated' && netKgPerPackage !== null) return calculatedTotalKg(quantity, netKgPerPackage);
    if (onHandQuantity <= 0) return 0;
    const scaled = (onHandNetWeightKg * quantity / onHandQuantity) * 1000;
    return Math.round(scaled + Number.EPSILON * Math.abs(scaled)) / 1000;
}
