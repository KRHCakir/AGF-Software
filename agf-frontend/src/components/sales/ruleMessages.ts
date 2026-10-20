import { MAX_ACTOR_LENGTH, MAX_CUSTOMER_REFERENCE_LENGTH, MAX_ORDER_LINES, MAX_REASON_LENGTH, RuleError } from '../../lib/salesOrderRules';
import { DecimalError } from '../../lib/decimal';
import { FieldErrors, addError, decimalErrorKey } from '../supply/messages';

type Translate = (key: string, variables?: Record<string, string>) => string;

const DECIMAL_CODES: readonly DecimalError[] = ['empty', 'invalid', 'not_positive', 'too_many_decimals', 'too_large'];

const MAX_LENGTHS: Record<string, number> = {
    customerReference: MAX_CUSTOMER_REFERENCE_LENGTH,
    actorReference: MAX_ACTOR_LENGTH,
    reason: MAX_REASON_LENGTH,
};

// Vertaling van een pure regelfout naar een melding; veldsleutels blijven gelijk aan die van de backend.
export function ruleMessage(error: RuleError, t: Translate): string {
    if ((DECIMAL_CODES as readonly string[]).includes(error.code)) {
        return t(decimalErrorKey(error.code as DecimalError));
    }
    switch (error.code) {
        case 'required':
            return error.field === 'actorReference' ? t('supply_error_actor_required')
                : error.field === 'goodsNeverLeftWarehouse' ? t('sales_correct_confirm_required')
                : error.field === 'acknowledgeExpectedSupply' ? t('sales_allocate_acknowledge_required')
                : error.field === 'requestedDeliveryDate' ? t('supply_error_required_date')
                : error.field === 'lines' ? t('supply_error_lines_required')
                : error.field.endsWith('.unit') ? t('supply_error_required_unit')
                : t('supply_error_required');
        case 'invalid_date': return t('supply_error_required_date');
        case 'too_long': return t('supply_error_too_long', { max: String(MAX_LENGTHS[error.field] ?? 100) });
        case 'too_many_lines': return t('sales_error_too_many_lines', { max: String(MAX_ORDER_LINES) });
        case 'duplicate_line': return t('sales_error_duplicate_line');
        case 'exceeds_available': return t('sales_error_exceeds_available');
        case 'exceeds_demand': return t('sales_error_exceeds_demand');
        case 'exceeds_remaining': return t('sales_error_exceeds_remaining');
        case 'remainder_mismatch': return t('sales_error_remainder_mismatch');
        default: return t('supply_error_required');
    }
}

export function ruleErrorsToFieldErrors(errors: RuleError[], t: Translate): FieldErrors {
    const found: FieldErrors = {};
    for (const error of errors) {
        addError(found, error.field, ruleMessage(error, t));
    }
    return found;
}
