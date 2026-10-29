// DOM-hulpen van de rondleiding: elementen vinden via data-tour en invullen met echte invoer- en toetsenbord-events
// (de native value-setter plus input/keydown), zodat zoekers, parsers en validaties precies zo reageren als bij een gebruiker.
// Nooit React-state of API-aanroepen om de UI heen. Pure DOM, geen React (te toetsen met jsdom).

import type { TourRect, TourTarget } from './types.ts';

// {{naam}} vervangen door de variabele; een onbekende variabele blijft zichtbaar staan, zodat een fout opvalt.
export function interpolate(text: string, vars: Readonly<Record<string, string>>): string {
    return text.replace(/\{\{(\w+)\}\}/g, (whole, name: string) => (Object.prototype.hasOwnProperty.call(vars, name) ? vars[name] : whole));
}

const NAME = /^[a-z0-9-]{1,60}$/;

// "naam" of "naam:sleutel" naar een selector op data-tour (en data-tour-key). De naam is altijd een vaste korte naam.
export function targetSelector(target: TourTarget, vars: Readonly<Record<string, string>>): string {
    const [name, ...rest] = target.split(':');
    if (!NAME.test(name)) throw new Error(`Invalid tour target: ${target}`);
    const key = rest.length > 0 ? interpolate(rest.join(':'), vars) : null;
    return key === null ? `[data-tour="${name}"]` : `[data-tour="${name}"][data-tour-key="${escapeAttribute(key)}"]`;
}

function escapeAttribute(value: string): string {
    return value.replace(/["\\]/g, '\\$&');
}

// Het eerste zichtbare element; jsdom kent geen layout, daar telt alleen of het in het document staat en niet verborgen is.
export function findTarget(doc: Document, target: TourTarget, vars: Readonly<Record<string, string>>): HTMLElement | null {
    const all = Array.from(doc.querySelectorAll<HTMLElement>(targetSelector(target, vars)));
    return all.find((element) => element.isConnected && !element.closest('[hidden]')) ?? null;
}

export function rectOf(element: Element): TourRect {
    const rect = element.getBoundingClientRect();
    return { top: rect.top, left: rect.left, width: rect.width, height: rect.height };
}

export function scrollIntoView(element: Element) {
    // jsdom heeft geen scrollIntoView.
    if (typeof (element as HTMLElement).scrollIntoView === 'function') element.scrollIntoView({ block: 'center', behavior: 'smooth' });
}

function viewOf(element: Element): Window & typeof globalThis {
    const view = element.ownerDocument.defaultView;
    if (!view) throw new Error('Element has no window');
    return view as Window & typeof globalThis;
}

type Editable = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

export function isEditable(element: Element): element is Editable {
    const view = viewOf(element);
    return element instanceof view.HTMLInputElement || element instanceof view.HTMLTextAreaElement || element instanceof view.HTMLSelectElement;
}

// De value-setter van het prototype: React volgt de waarde zelf en ziet een gewone toewijzing anders niet als wijziging.
export function setNativeValue(element: Editable, value: string) {
    const view = viewOf(element);
    const proto = element instanceof view.HTMLSelectElement ? view.HTMLSelectElement.prototype
        : element instanceof view.HTMLTextAreaElement ? view.HTMLTextAreaElement.prototype : view.HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
    if (!setter) throw new Error('No native value setter');
    setter.call(element, value);
}

export function dispatchInput(element: Editable) {
    const view = viewOf(element);
    element.dispatchEvent(new view.Event('input', { bubbles: true }));
}

export function dispatchChange(element: Editable) {
    const view = viewOf(element);
    element.dispatchEvent(new view.Event('change', { bubbles: true }));
}

// Een toets zoals het toetsenbord hem stuurt (keydown en keyup); geeft false als een handler de standaardactie blokkeerde.
export function pressKey(element: Element, key: string): boolean {
    const view = viewOf(element);
    const down = new view.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    const proceed = element.dispatchEvent(down);
    element.dispatchEvent(new view.KeyboardEvent('keyup', { key, bubbles: true, cancelable: true }));
    return proceed;
}

export function focus(element: HTMLElement) {
    if (element.ownerDocument.activeElement !== element) element.focus();
}

// Velden die in één keer een waarde krijgen (zoals een gebruiker een datum kiest), in plaats van letter voor letter.
export function typesAtOnce(element: Editable): boolean {
    const view = viewOf(element);
    if (element instanceof view.HTMLSelectElement) return true;
    return element instanceof view.HTMLInputElement && ['date', 'datetime-local', 'time', 'month', 'week', 'color', 'range'].includes(element.type);
}

// Eén letter: keydown, de waarde uitbreiden, input, keyup.
export function typeCharacter(element: HTMLInputElement | HTMLTextAreaElement, character: string) {
    const view = viewOf(element);
    element.dispatchEvent(new view.KeyboardEvent('keydown', { key: character, bubbles: true, cancelable: true }));
    setNativeValue(element, element.value + character);
    dispatchInput(element);
    element.dispatchEvent(new view.KeyboardEvent('keyup', { key: character, bubbles: true, cancelable: true }));
}

// Het veld leegmaken zoals selecteren + Backspace: één input-event met een lege waarde.
export function clearValue(element: HTMLInputElement | HTMLTextAreaElement) {
    if (element.value === '') return;
    setNativeValue(element, '');
    dispatchInput(element);
}

export function setValueAtOnce(element: Editable, value: string) {
    setNativeValue(element, value);
    dispatchInput(element);
    dispatchChange(element);
}

export function normalizeText(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/\s+/g, ' ').toLowerCase().trim();
}

// Zichtbare tekst of waarde van een element (voor assert en remember).
export function textOf(element: HTMLElement): string {
    return isEditable(element) ? element.value : element.textContent ?? '';
}

export function isDisabled(element: HTMLElement): boolean {
    return (element as HTMLButtonElement).disabled === true || element.getAttribute('aria-disabled') === 'true';
}
