import { DecimalError } from '../../lib/decimal';
import { ApiError } from '../../services/apiClient';

// i18n-sleutel per validatiefout van een decimaal veld.
export function decimalErrorKey(error: DecimalError): string {
    switch (error) {
        case 'empty': return 'supply_error_required';
        case 'invalid': return 'supply_error_decimal_invalid';
        case 'separator': return 'supply_error_decimal_separator';
        case 'not_positive':
        case 'negative': return 'supply_error_decimal_not_positive';
        case 'too_many_decimals': return 'supply_error_decimal_too_many';
        default: return 'supply_error_decimal_too_large';
    }
}

// Palletinvoer: een breuk waar alleen hele aantallen kunnen (3,5 pallets), anders de gewone getalfout.
export function palletErrorKey(field: string, code: string): string {
    if (code !== 'not_whole') return decimalErrorKey(code as DecimalError);
    return field.endsWith('palletCount') ? 'pallet_error_whole_pallets' : 'pallet_error_whole_number';
}

// Onbekende uitkomst: het verzoek kan bij de server zijn aangekomen. Dan alleen ongewijzigd opnieuw proberen.
// Bij een configuratiefout is niets verstuurd; bij 4xx heeft de server het verzoek definitief geweigerd.
export function outcomeUnknown(error: ApiError): boolean {
    return !(error.kind === 'config' || error.isDefinitiveRejection);
}

export type FieldErrors = Record<string, string[]>;

export function addError(errors: FieldErrors, key: string, message: string) {
    (errors[key] ??= []).push(message);
}
