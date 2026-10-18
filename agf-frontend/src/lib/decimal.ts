// Invoer van hoeveelheden en gewichten zoals de backend ze accepteert: positief, hooguit 3 decimalen,
// binnen decimal(18,3). Komma en punt zijn allebei toegestaan als decimaalteken; geen scheidingstekens voor duizendtallen.
// Er wordt nooit afgerond, omgerekend of een waarde verzonnen.

export type DecimalError = 'empty' | 'invalid' | 'not_positive' | 'too_many_decimals' | 'too_large';

export type DecimalResult = { ok: true; value: number } | { ok: false; error: DecimalError };

export function parsePositiveDecimal(input: string): DecimalResult {
    const text = input.trim();
    if (text === '') {
        return { ok: false, error: 'empty' };
    }
    const match = /^(\d+)(?:[.,](\d+))?$/.exec(text);
    if (match === null) {
        return { ok: false, error: 'invalid' };
    }
    const [, integerPart, fractionPart = ''] = match;
    if (fractionPart.length > 3) {
        return { ok: false, error: 'too_many_decimals' };
    }
    if (integerPart.replace(/^0+(?=\d)/, '').length > 15) {
        return { ok: false, error: 'too_large' };
    }
    const value = Number(fractionPart === '' ? integerPart : `${integerPart}.${fractionPart}`);
    if (!(value > 0)) {
        return { ok: false, error: 'not_positive' };
    }
    return { ok: true, value };
}
