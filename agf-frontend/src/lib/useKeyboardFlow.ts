'use client';

import { RefObject, useEffect, useRef } from 'react';
import { FlowAction, adjacentField, fieldKind, flowAction, flowFields, firstInvalidField } from './keyboardFlow.ts';
import { isMacPlatform } from './shortcuts.ts';

// Herbruikbare toetsenbordflow voor een invoerformulier (regels in keyboardFlow.ts): Enter en pijl links/rechts aan het begin/einde
// naar het volgende/vorige veld, ⌘S/Ctrl+S slaat op (ook als de focus buiten het formulier staat, zoals voorheen), getalvelden met
// data-select-on-focus selecteren hun inhoud bij focus zodat je kunt overtypen. Geeft focusFirstInvalid terug: na een validatiefout
// de focus op het eerste ongeldige veld in visuele volgorde.
export function useKeyboardFlow(formRef: RefObject<HTMLElement | null>, options: { onSave: () => void }) {
    const onSave = useRef(options.onSave);
    onSave.current = options.onSave;

    useEffect(() => {
        const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
        const act = (event: KeyboardEvent, action: FlowAction, target: HTMLElement) => {
            if (action === null) return;
            event.preventDefault();
            if (action === 'save') { onSave.current(); return; }
            if (action === 'block') return;
            const form = formRef.current;
            if (form === null) return;
            const next = adjacentField(flowFields<HTMLElement & { disabled?: boolean }>(form), target, action);
            if (next === null) return;
            next.focus();
            // Met een pijl verder: cursor aan de kant waar je binnenkomt (links erin = einde, rechts erin = begin), zodat dezelfde pijl
            // meteen weer door kan. Getalvelden met data-select-on-focus houden hun selectie (overtypen).
            const input = next as HTMLInputElement;
            if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && !next.hasAttribute('data-select-on-focus') && typeof input.setSelectionRange === 'function') {
                const position = event.key === 'ArrowLeft' ? (input.value ?? '').length : 0;
                try { input.setSelectionRange(position, position); } catch { /* type zonder selectie (bijv. e-mail in sommige browsers) */ }
            }
        };
        // Opslaan: op documentniveau in de capture-fase (zoals de bestaande ⌘S/Ctrl+S), zodat het overal in het scherm werkt.
        const onDocumentKey = (event: KeyboardEvent) => {
            if (flowAction(event, { kind: 'other', selectionStart: null, selectionEnd: null, valueLength: 0 }, isMac) === 'save') {
                event.preventDefault();
                onSave.current();
            }
        };
        const onFormKey = (event: KeyboardEvent) => {
            const target = event.target as (HTMLElement & { type?: string; value?: string; selectionStart?: number | null; selectionEnd?: number | null }) | null;
            if (target === null || event.defaultPrevented) return;
            const kind = fieldKind(target);
            if (kind === 'other') return;
            let action = flowAction(event, { kind, selectionStart: target.selectionStart ?? null, selectionEnd: target.selectionEnd ?? null, valueLength: (target.value ?? '').length }, isMac);
            if (action === 'save') return; // al afgehandeld op documentniveau
            // Enter zonder volgend veld: niet versturen, niets doen.
            if (action === 'next' && event.key === 'Enter' && formRef.current !== null && adjacentField(flowFields<HTMLElement & { disabled?: boolean }>(formRef.current), target, 'next') === null) action = 'block';
            act(event, action, target);
        };
        const onFocusIn = (event: FocusEvent) => {
            const target = event.target as HTMLInputElement | null;
            if (target?.hasAttribute?.('data-select-on-focus') && typeof target.select === 'function') target.select();
        };
        const form = formRef.current;
        document.addEventListener('keydown', onDocumentKey, true);
        form?.addEventListener('keydown', onFormKey);
        form?.addEventListener('focusin', onFocusIn);
        return () => {
            document.removeEventListener('keydown', onDocumentKey, true);
            form?.removeEventListener('keydown', onFormKey);
            form?.removeEventListener('focusin', onFocusIn);
        };
    }, [formRef]);

    return {
        // Na het openen van de secties met fouten (state-update) pas zoeken: één macrotaak later is de DOM bijgewerkt.
        focusFirstInvalid: (fallbackId?: string) => {
            setTimeout(() => {
                const form = formRef.current;
                const invalid = form === null ? null : firstInvalidField<HTMLElement & { disabled?: boolean }>(form);
                (invalid ?? (fallbackId ? document.getElementById(fallbackId) : null))?.focus();
            }, 0);
        },
    };
}
