// Toetsenbordflow van een invoerformulier (pure logica, getest met node:test; de React-kant staat in useKeyboardFlow.ts).
//
//   - Tab / Shift+Tab: de browser zelf. Het formulier zorgt dat de DOM-volgorde de visuele volgorde is, ingeklapte secties niet
//     gerenderd zijn en uitgeschakelde velden (ook via een uitgeschakelde fieldset) overgeslagen worden; flowFields geeft precies
//     die volgorde (zonder knoppen) en is daarmee ook de controle in de tests.
//   - Enter in een veld (tekst, getal-als-tekst, datum, keuzelijst, vinkje) gaat naar het volgende veld en verstuurt nooit het
//     formulier; in het laatste veld gebeurt niets (opslaan blijft ⌘S/Ctrl+S). In een textarea maakt Enter een nieuwe regel.
//   - Pijl links/rechts in tekst en textarea: naar het vorige/volgende veld als de cursor (zonder selectie) aan het begin/einde
//     staat of het veld leeg is; midden in de tekst en bij een selectie beweegt de cursor gewoon. Een textarea gedraagt zich hier als
//     een tekstveld: aan het begin/einde doet de pijl daar zelf niets, dus er gaat geen bewerking verloren. Keuzelijsten, vinkjes en
//     datumvelden houden hun eigen pijltjesgedrag (waarde of datumdeel kiezen).
//   - Met Shift, Ctrl, Alt of ⌘ erbij grijpt de flow nooit in (selecteren, woordsprongen), behalve ⌘S/Ctrl+S = opslaan.
//   - Tijdens IME-compositie (bijv. accenten) grijpt de flow niet in.

import { matchShortcut } from './shortcuts.ts';
import type { KeyLike } from './shortcuts.ts';

export type FlowAction = 'next' | 'previous' | 'save' | 'block' | null;
export type FieldKind = 'text' | 'textarea' | 'select' | 'toggle' | 'picker' | 'other';

export interface FlowKey extends KeyLike { isComposing?: boolean }

export interface FlowTarget {
    kind: FieldKind;
    selectionStart: number | null;
    selectionEnd: number | null;
    valueLength: number;
}

const TEXT_TYPES = new Set(['text', 'search', 'email', 'tel', 'url', 'password']);
const TOGGLE_TYPES = new Set(['checkbox', 'radio']);
// Velden met eigen pijltjesgedrag (datumdelen, tijd, kleur, bereik, getal).
const PICKER_TYPES = new Set(['date', 'datetime-local', 'month', 'week', 'time', 'number', 'range', 'color', 'file']);

export interface ElementLike {
    tagName: string;
    type?: string;
}

export function fieldKind(element: ElementLike): FieldKind {
    const tag = element.tagName.toUpperCase();
    if (tag === 'TEXTAREA') return 'textarea';
    if (tag === 'SELECT') return 'select';
    if (tag !== 'INPUT') return 'other';
    const type = (element.type ?? 'text').toLowerCase();
    if (TEXT_TYPES.has(type)) return 'text';
    if (TOGGLE_TYPES.has(type)) return 'toggle';
    if (PICKER_TYPES.has(type)) return 'picker';
    return 'other';
}

// Wat een toets in een veld doet. 'block' = alleen de standaardactie tegenhouden (Enter in het laatste veld: niet versturen).
export function flowAction(event: FlowKey, target: FlowTarget, isMac: boolean): FlowAction {
    if (matchShortcut(event, isMac) === 'save') return 'save';
    if (event.isComposing) return null;
    if (event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return null;
    const textual = target.kind === 'text' || target.kind === 'textarea';
    if (event.key === 'Enter') {
        return target.kind === 'text' || target.kind === 'select' || target.kind === 'toggle' || target.kind === 'picker' ? 'next' : null;
    }
    if (!textual || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return null;
    const { selectionStart: start, selectionEnd: end, valueLength: length } = target;
    if (length === 0) return event.key === 'ArrowLeft' ? 'previous' : 'next';
    if (start === null || end === null || start !== end) return null;
    if (event.key === 'ArrowLeft' && start === 0) return 'previous';
    if (event.key === 'ArrowRight' && end === length) return 'next';
    return null;
}

// Minimale DOM-vorm, zodat de volgorde ook zonder browser te toetsen is.
export interface FlowElement extends ElementLike {
    disabled?: boolean;
    tabIndex: number;
    matches(selector: string): boolean;
    closest(selector: string): unknown;
}

export function isFlowField(element: FlowElement): boolean {
    if (fieldKind(element) === 'other') return false;
    if (element.disabled || element.matches(':disabled')) return false;
    if (element.tabIndex < 0) return false;
    return element.closest('[hidden], [inert], [aria-hidden="true"]') === null;
}

// Velden in tabvolgorde (DOM-volgorde; dit formulier gebruikt geen positieve tabIndex).
export function flowFields<T extends FlowElement>(root: { querySelectorAll(selector: string): ArrayLike<T> }): T[] {
    return Array.from(root.querySelectorAll('input, select, textarea')).filter(isFlowField);
}

export function adjacentField<T>(fields: readonly T[], current: T, direction: 'next' | 'previous'): T | null {
    const index = fields.indexOf(current);
    if (index < 0) return null;
    return fields[direction === 'next' ? index + 1 : index - 1] ?? null;
}

// Eerste ongeldige veld in visuele volgorde (aria-invalid), voor de focus na een validatiefout.
export function firstInvalidField<T extends FlowElement & { getAttribute(name: string): string | null }>(root: { querySelectorAll(selector: string): ArrayLike<T> }): T | null {
    return flowFields(root).find((field) => field.getAttribute('aria-invalid') === 'true') ?? null;
}
