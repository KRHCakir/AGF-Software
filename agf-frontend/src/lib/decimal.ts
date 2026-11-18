// Getalinvoer in de notatie van de schermtaal, één regel voor de hele software. Nederlands: komma = decimaalteken, punt =
// duizendtallen ("15.600,0"); Engels andersom. De regels (voorbeelden in het Nederlands):
// - een enkele punt gevolgd door 1 of 2 cijfers aan het eind (7.5, 7.50, 0.79) is een decimaalteken: 7,5;
// - een punt in kloppende groepen van 3 (15.000, 1.234.567,5) is een duizendtal; ook met spaties of apostrofs (15 000, 15'000);
// - alles daartussen (2.272.5, 15.0000, 0.500, 15,000.5) is ongeldig, nooit een gok; waar het kan met een aanbevolen waarde.
// Nooit afronden; de backend krijgt gewoon een JSON-getal.

export type NumberLocale = 'nl' | 'en';

export type DecimalError = 'empty' | 'invalid' | 'separator' | 'not_positive' | 'negative' | 'too_many_decimals' | 'too_large';

// suggestion: bij een ongeldige vorm de waarde die de gebruiker waarschijnlijk bedoelt, in de notatie van de schermtaal ("2.272,5").
export type DecimalResult = { ok: true; value: number } | { ok: false; error: DecimalError; suggestion?: string };

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

const separators = (locale: NumberLocale): [decimal: string, group: string] => (locale === 'nl' ? [',', '.'] : ['.', ',']);
const escape = (char: string) => `\\${char}`;
// Spatie (ook hard, smal en dun) en apostrof als duizendtalteken, zoals in geplakte getallen (15 000,5 en 15'000,5).
const OTHER_GROUP = /[\s'’]/;

type DecimalText = { ok: true; negative: boolean; integer: string; fraction: string } | { ok: false; error: 'empty' | 'invalid' | 'separator'; suggestion?: string };

const groupDigits = (digits: string, group: string) => digits.replace(/\B(?=(\d{3})+$)/g, group);

// Het hele deel: alleen cijfers, of kloppende groepen van 3 met één soort teken en een eerste groep zonder voorloopnul.
function wholePart(text: string, group: string): string | null {
    if (/^\d+$/.test(text)) return text;
    const used = text.includes(group) ? escape(group) : OTHER_GROUP.source;
    return new RegExp(`^[1-9]\\d{0,2}(?:${used}\\d{3})+$`).test(text) ? text.replace(/\D/g, '') : null;
}

// De vorm van de invoer, zonder grenzen (positief, aantal decimalen): cijfers voor en na het decimaalteken, of waarom het niet klopt.
function readDecimalText(input: string, locale: NumberLocale, trySwapped = true): DecimalText {
    let text = input.trim();
    if (text === '') return { ok: false, error: 'empty' };
    const negative = /^[-−]/.test(text);
    if (negative) text = text.slice(1).trim();
    const [decimal, group] = separators(locale);
    const sign = negative ? '-' : '';
    const shown = (integer: string, fraction: string) => `${sign}${groupDigits(integer.replace(/^0+(?=\d)/, ''), group)}${fraction === '' ? '' : decimal + fraction}`;

    const parts = text.split(decimal);
    if (parts.length > 2) return { ok: false, error: 'invalid' };
    if (parts.length === 2) {
        const [head, fraction] = parts;
        const integer = head === '' && fraction !== '' ? '0' : wholePart(head, group);
        if (integer !== null && /^\d*$/.test(fraction)) return { ok: true, negative, integer, fraction };
        // Punt en komma die niet kloppen (15,000.5 in het Nederlands): klopt het in de andere notatie, dan is dat de aanbeveling.
        const other: NumberLocale = locale === 'nl' ? 'en' : 'nl';
        const swapped = trySwapped && text.includes(group) ? readDecimalText(text, other, false) : null;
        if (swapped?.ok && swapped.fraction !== '') return { ok: false, error: 'separator', suggestion: shown(swapped.integer, swapped.fraction) };
        return { ok: false, error: integer === null && head.includes(group) ? 'separator' : 'invalid' };
    }

    // Zonder decimaalteken: een heel getal, een duizendtal, of één groepsteken dat als decimaalteken bedoeld is.
    const whole = wholePart(text, group);
    if (whole !== null) return { ok: true, negative, integer: whole, fraction: '' };
    if (!text.includes(group)) return { ok: false, error: 'invalid' };
    const last = text.lastIndexOf(group);
    const [before, after] = [text.slice(0, last), text.slice(last + 1)];
    if (!/^\d*$/.test(after)) return { ok: false, error: 'invalid' };
    // "5." en "15.000.": een teken aan het eind met niets erachter telt niet mee.
    if (after === '') {
        const integer = wholePart(before, group);
        return integer === null ? { ok: false, error: 'separator' } : { ok: true, negative, integer, fraction: '' };
    }
    // Eén teken met 1 of 2 cijfers erachter (7.5, 7.50, .5): het decimaalteken.
    if (/^\d*$/.test(before) && after.length <= 2) return { ok: true, negative, integer: before === '' ? '0' : before, fraction: after };
    // Anders ongeldig (2.272.5, 15.0000, 0.500); klopt het deel vóór het laatste teken, dan is dat teken waarschijnlijk de komma.
    const integer = before === '' ? '0' : wholePart(before, group);
    return integer === null ? { ok: false, error: 'separator' } : { ok: false, error: 'separator', suggestion: shown(integer, after) };
}

export function parseLocaleDecimal(input: string, locale: NumberLocale = inputLocale, options: DecimalOptions = {}): DecimalResult {
    const { maxDecimals = 3, maxDigits = 15, allowZero = false, allowNegative = false } = options;
    const text = readDecimalText(input, locale);
    if (!text.ok) return text;
    if (text.fraction.length > maxDecimals) return { ok: false, error: 'too_many_decimals' };
    if (text.integer.replace(/^0+(?=\d)/, '').length > maxDigits) return { ok: false, error: 'too_large' };
    const magnitude = Number(text.fraction === '' ? text.integer : `${text.integer}.${text.fraction}`);
    if (text.negative && magnitude !== 0 && !allowNegative) return { ok: false, error: allowZero ? 'negative' : 'not_positive' };
    if (magnitude === 0 && !allowZero) return { ok: false, error: 'not_positive' };
    return { ok: true, value: text.negative && magnitude !== 0 ? -magnitude : magnitude };
}

// Positief, hooguit 3 decimalen, binnen decimal(18,3): hoeveelheden en gewichten zoals de backend ze accepteert.
export function parsePositiveDecimal(input: string, locale: NumberLocale = inputLocale): DecimalResult {
    return parseLocaleDecimal(input, locale);
}

// Getal als invoertekst in de schermtaal (15000 -> "15.000", 4.25 -> "4,25"); voor voorinvullen en herschrijven na blur.
// minDecimals: het vaste aantal decimalen van het veld (4 -> "4,00"); er wordt nooit afgerond onder maxDecimals.
export function formatDecimalInput(value: number, maxDecimals = 3, locale: NumberLocale = inputLocale, minDecimals = 0): string {
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: Math.min(minDecimals, maxDecimals), maximumFractionDigits: maxDecimals }).format(value);
}

// Geldbedrag als invoertekst, altijd met twee decimalen (500 -> "500,00", 5000 -> "5.000,00").
export function formatAmountInput(value: number, locale: NumberLocale = inputLocale): string {
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

// Kilo's als invoertekst: altijd minstens twee decimalen (4 -> "4,00", 1,125 blijft "1,125"), zoals het veld ze na het verlaten toont.
export function formatKgInput(value: number, locale: NumberLocale = inputLocale): string {
    return formatDecimalInput(value, 3, locale, 2);
}

// Na het verlaten van een veld: geldige invoer in de notatie van de schermtaal, met minstens het vaste aantal decimalen van het
// veld en nooit minder dan getypt (4 -> 4,00; 15000 -> 15.000; 7.5 -> 7,50; 15600,125 blijft 15.600,125). Er wordt niets afgerond:
// de cijfers blijven zoals getypt. Ongeldige invoer blijft staan zoals getypt.
export function reformatDecimal(input: string, locale: NumberLocale = inputLocale, fixedDecimals = 0): string {
    const text = readDecimalText(input, locale);
    if (!text.ok) return input;
    const [decimal, group] = separators(locale);
    const integer = text.integer.replace(/^0+(?=\d)/, '');
    const fraction = text.fraction.padEnd(fixedDecimals, '0');
    const zero = /^0*$/.test(integer + fraction);
    return `${text.negative && !zero ? '-' : ''}${groupDigits(integer, group)}${fraction === '' ? '' : decimal + fraction}`;
}

// Tijdens het typen of plakken: één punt met 1 of 2 cijfers erachter aan het eind is het decimaalteken en wordt meteen een komma
// (7.5 -> 7,5; .5 -> ,5). Al het andere blijft staan zoals getypt.
export function typedDecimalSeparator(text: string, locale: NumberLocale = inputLocale): string {
    const [decimal, group] = separators(locale);
    const match = new RegExp(`^(\\s*[-−]?\\s*\\d*)${escape(group)}(\\d{1,2}\\s*)$`).exec(text);
    return match ? `${match[1]}${decimal}${match[2]}` : text;
}

// Wie een duizendtal met de punt typt (15.000) komt onderweg langs 15.0 en 15.00, die al een komma kregen. Zodra er precies drie
// cijfers achter staan, wordt het weer de punt die getypt is: 15,000 -> 15.000. Ook als dat geen kloppend duizendtal is (0.500,
// 1234.567): de punt blijft dan staan en de invoer is ongeldig, net als bij plakken; nooit stilzwijgend een ander geldig getal.
export function typedThousands(text: string, locale: NumberLocale = inputLocale): string {
    const [decimal, group] = separators(locale);
    const match = new RegExp(`^(\\s*[-−]?\\s*\\d+)${escape(decimal)}(\\d{3})$`).exec(text);
    return match ? `${match[1]}${group}${match[2]}` : text;
}

// Getalvelden herkennen: tekstvelden met inputMode "decimal". data-decimals = het vaste aantal decimalen van het veld.
export function decimalField(target: EventTarget | null, view: Window & typeof globalThis): HTMLInputElement | null {
    return target instanceof view.HTMLInputElement && target.getAttribute('inputmode') === 'decimal' && !target.readOnly && !target.disabled ? target : null;
}

// De waarde van een veld zetten zoals een gebruiker dat doet: de eigen onChange van het veld (React) ziet de nieuwe tekst.
export function setFieldValue(input: HTMLInputElement, value: string, announce = true) {
    const view = input.ownerDocument.defaultView!;
    Object.getOwnPropertyDescriptor(view.HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    if (announce) input.dispatchEvent(new view.Event('input', { bubbles: true }));
}

// Eén luisteraar per pagina, voor alle getalvelden (inputMode "decimal"):
// - tijdens het typen en plakken wordt de punt als decimaalteken meteen een komma (typedDecimalSeparator), vóór de eigen onChange
//   van het veld, zodat ook het totaal direct meerekent;
// - na het verlaten krijgt een gewijzigd veld de notatie van de schermtaal met zijn vaste decimalen (data-decimals).
// Een veld dat alleen is aangeklikt en niet gewijzigd, blijft onaangeroerd: een berekende waarde wordt zo nooit "ingevuld".
export function reformatDecimalFieldsOnBlur(root: Document): () => void {
    const view = root.defaultView!;
    const atFocus = new WeakMap<HTMLInputElement, string>();
    // Per veld de tekst die hier van een getypte punt een komma kreeg. Alleen als de gebruiker daar daarna precies één cijfer achter
    // typt, hoort het nog bij dezelfde invoer; vervangen of plakken (15,000 over 7,5 heen) begint opnieuw en wordt nooit omgezet.
    const fromGroup = new WeakMap<HTMLInputElement, string>();
    const onFocusIn = (event: Event) => {
        const input = decimalField(event.target, view);
        if (input) atFocus.set(input, input.value);
    };
    const onInput = (event: Event) => {
        const input = decimalField(event.target, view);
        if (!input) return;
        const typed = typedDecimalSeparator(input.value);
        const before = fromGroup.get(input);
        const oneDigitMore = before !== undefined && input.value.length === before.length + 1 && input.value.startsWith(before) && /\d$/.test(input.value);
        const next = typed !== input.value ? typed : oneDigitMore ? typedThousands(input.value) : input.value;
        if (typed !== input.value) fromGroup.set(input, typed);
        else if (oneDigitMore && next === input.value) fromGroup.set(input, input.value);
        else fromGroup.delete(input);
        if (next === input.value) return;
        const [start, end] = [input.selectionStart, input.selectionEnd];
        // Zonder eigen event: dit loopt vóór de onChange van het veld, die daarna de nieuwe tekst leest.
        setFieldValue(input, next, false);
        try { input.setSelectionRange(start, end); } catch { /* veldsoort zonder selectie */ }
    };
    const onFocusOut = (event: Event) => {
        const input = decimalField(event.target, view);
        if (!input) return;
        fromGroup.delete(input);
        const before = atFocus.get(input);
        atFocus.delete(input);
        if (before === input.value) return;
        const next = reformatDecimal(input.value, undefined, Number(input.dataset.decimals ?? 0) || 0);
        if (next !== input.value) setFieldValue(input, next);
    };
    root.addEventListener('focusin', onFocusIn);
    root.addEventListener('input', onInput, true);
    root.addEventListener('focusout', onFocusOut);
    return () => {
        root.removeEventListener('focusin', onFocusIn);
        root.removeEventListener('input', onInput, true);
        root.removeEventListener('focusout', onFocusOut);
    };
}

// Voor de melding bij een veld: null als de vorm klopt (of het veld leeg is), anders de aanbevolen waarde (of geen).
export function decimalShapeProblem(input: string, locale: NumberLocale = inputLocale): { suggestion: string | null } | null {
    const text = readDecimalText(input, locale);
    return text.ok || text.error === 'empty' ? null : { suggestion: text.suggestion ?? null };
}
