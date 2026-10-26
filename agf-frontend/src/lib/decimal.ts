// Getalinvoer in de notatie van de schermtaal. Nederlands: komma = decimaalteken, punt = duizendtallen ("15.600,0"); Engels andersom.
// Strikt: duizendtalscheiding alleen in kloppende groepen van 3 met een eerste groep zonder voorloopnul (15.000, 1.234.567,5);
// een punt die dat niet doet (15.5, 1.23, 15.0000, 0.500) is in het Nederlands een fout, nooit een gok. Spaties (ook harde en
// smalle) gelden als duizendtalscheiding onder dezelfde groepsregel. Nooit afronden; de backend krijgt gewoon een JSON-getal.

export type NumberLocale = 'nl' | 'en';

export type DecimalError = 'empty' | 'invalid' | 'separator' | 'not_positive' | 'negative' | 'too_many_decimals' | 'too_large';

export type DecimalResult = { ok: true; value: number } | { ok: false; error: DecimalError };

export interface DecimalOptions {
    maxDecimals?: number;
    // Aantal cijfers vóór de komma (zonder voorloopnullen).
    maxDigits?: number;
    allowZero?: boolean;
    allowNegative?: boolean;
}

// Schermtaal voor getalinvoer, gezet door de LanguageProvider; zo lezen pure regels zonder extra parameter in de taal van het scherm.
let inputLocale: NumberLocale = 'nl';

export function setInputLocale(locale: NumberLocale) {
    inputLocale = locale;
}

export function parseLocaleDecimal(input: string, locale: NumberLocale = inputLocale, options: DecimalOptions = {}): DecimalResult {
    const { maxDecimals = 3, maxDigits = 15, allowZero = false, allowNegative = false } = options;
    let text = input.trim();
    if (text === '') return { ok: false, error: 'empty' };
    const negative = /^[-−]/.test(text);
    if (negative) text = text.slice(1).trim();
    const [decimal, group] = locale === 'nl' ? [',', '.'] : ['.', ','];
    const parts = text.split(decimal);
    if (parts.length > 2) return { ok: false, error: 'invalid' };
    const [integerText, fractionPart] = parts;
    if (fractionPart !== undefined && !/^\d+$/.test(fractionPart)) return { ok: false, error: 'invalid' };
    let integerPart = integerText;
    if (!/^\d+$/.test(integerText)) {
        const separator = integerText.includes(group) ? `\\${group}` : '\\s';
        if (!new RegExp(`^[1-9]\\d{0,2}(?:${separator}\\d{3})+$`).test(integerText)) {
            return { ok: false, error: integerText.includes(group) ? 'separator' : 'invalid' };
        }
        integerPart = integerText.replace(/\D/g, '');
    }
    if ((fractionPart ?? '').length > maxDecimals) return { ok: false, error: 'too_many_decimals' };
    if (integerPart.replace(/^0+(?=\d)/, '').length > maxDigits) return { ok: false, error: 'too_large' };
    const magnitude = Number(fractionPart === undefined ? integerPart : `${integerPart}.${fractionPart}`);
    if (negative && magnitude !== 0 && !allowNegative) return { ok: false, error: allowZero ? 'negative' : 'not_positive' };
    if (magnitude === 0 && !allowZero) return { ok: false, error: 'not_positive' };
    return { ok: true, value: negative && magnitude !== 0 ? -magnitude : magnitude };
}

// Positief, hooguit 3 decimalen, binnen decimal(18,3): hoeveelheden en gewichten zoals de backend ze accepteert.
export function parsePositiveDecimal(input: string, locale: NumberLocale = inputLocale): DecimalResult {
    return parseLocaleDecimal(input, locale);
}

// Getal als invoertekst in de schermtaal (15000 -> "15.000", 4.25 -> "4,25"); voor voorinvullen en herschrijven na blur.
export function formatDecimalInput(value: number, maxDecimals = 3, locale: NumberLocale = inputLocale): string {
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { maximumFractionDigits: maxDecimals }).format(value);
}

// Na het verlaten van een veld: geldige invoer in de notatie van de schermtaal, met evenveel decimalen als getypt
// (15000 -> 15.000, 15600,0 -> 15.600,0); ongeldige invoer blijft staan zoals getypt.
export function reformatDecimal(input: string, locale: NumberLocale = inputLocale): string {
    const parsed = parseLocaleDecimal(input, locale);
    if (!parsed.ok) return input;
    const decimals = input.trim().split(locale === 'nl' ? ',' : '.')[1]?.length ?? 0;
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(parsed.value);
}

// Eén luisteraar per pagina: een getalveld (inputMode "decimal") krijgt na blur de notatie van de schermtaal. De tekst gaat als
// gewoon input-event door de eigen onChange van het veld; ongeldige, alleen-lezen of uitgeschakelde velden blijven onaangeroerd.
export function reformatDecimalFieldsOnBlur(root: Document): () => void {
    const view = root.defaultView!;
    const onFocusOut = (event: Event) => {
        const input = event.target;
        if (!(input instanceof view.HTMLInputElement) || input.getAttribute('inputmode') !== 'decimal' || input.readOnly || input.disabled) return;
        const next = reformatDecimal(input.value);
        if (next === input.value) return;
        Object.getOwnPropertyDescriptor(view.HTMLInputElement.prototype, 'value')!.set!.call(input, next);
        input.dispatchEvent(new view.Event('input', { bubbles: true }));
    };
    root.addEventListener('focusout', onFocusOut);
    return () => root.removeEventListener('focusout', onFocusOut);
}
