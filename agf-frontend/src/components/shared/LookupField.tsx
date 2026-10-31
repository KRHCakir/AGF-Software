'use client';

import { CSSProperties, KeyboardEvent, ReactNode, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { inputClass } from '../supply/FieldError';

// Gedeeld zoekveld (type-ahead) voor producten, leveranciers en latere lookups. Toetsenbord: pijltjes navigeren, Enter
// kiest, Escape sluit eerst de lijst. Tab accepteert alleen een duidelijke suggestie (één treffer, een exact gemarkeerde
// treffer, of een met de pijltjes gekozen regel) en verlaat het veld altijd: nooit een toetsenbordval, nooit een stille
// keuze bij ambigue invoer. Een keuze verandert alleen het veld; opslaan gebeurt nooit vanuit dit onderdeel.
// Met `allowFreeText` blijft getypte tekst zonder keuze staan als vrije waarde (bijvoorbeeld een vrije omschrijving).

export interface LookupMatch<T> {
    key: string;
    item: T;
    description: string;
    // Duidelijke treffer (bijvoorbeeld een exacte snelcode): mag met Tab worden overgenomen zonder pijltjesnavigatie.
    exact: boolean;
    badge?: string | null;
    badgeStrong?: boolean;
}

export const MAX_LOOKUP_SUGGESTIONS = 12;

// Precies één treffer, of precies één exacte treffer, is een duidelijke suggestie.
export function unambiguousLookup<T>(matches: readonly LookupMatch<T>[]): LookupMatch<T> | null {
    if (matches.length === 1) return matches[0];
    const exact = matches.filter((match) => match.exact);
    return exact.length === 1 ? exact[0] : null;
}

// Volgende focusbare element na het zoekveld in hetzelfde formulier (na Enter op een suggestie, zoals Tab dat zou doen).
function focusNextAfter(input: HTMLInputElement) {
    const scope: ParentNode = input.form ?? input.ownerDocument;
    const focusable = Array.from(scope.querySelectorAll<HTMLElement>('input, select, textarea, button, a[href]'))
        .filter((element) => element.tabIndex >= 0 && !(element as HTMLInputElement).disabled && (element as HTMLInputElement).type !== 'hidden');
    focusable[focusable.indexOf(input) + 1]?.focus();
}

export default function LookupField<T>({ id, ariaLabel, selected, search, onSelect, onClear, onFreeText, freeText = '', allowFreeText = false, disabled = false, invalid = false, describedBy, placeholderKey, hintKey, noneKey, moreKey, changeKey, inputRef: externalRef, className = '', renderExtra, maxSuggestions = MAX_LOOKUP_SUGGESTIONS, takeHighlighted = false, onQueryChange, dataTour, dataTourKey }: {
    // Het invoerveld krijgt `${id}-input`, zoals fieldProps() dat voor labels en foutmeldingen doet.
    id: string;
    ariaLabel: string;
    // De gekozen waarde met haar volledige omschrijving; null = niets gekozen.
    selected: { description: string } | null;
    search: (query: string) => LookupMatch<T>[];
    onSelect: (item: T) => void;
    onClear: () => void;
    // Alleen met allowFreeText: de getypte tekst zodra het veld wordt verlaten zonder keuze.
    onFreeText?: (text: string) => void;
    freeText?: string;
    allowFreeText?: boolean;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    placeholderKey: string;
    hintKey: string;
    noneKey: string;
    moreKey: string;
    changeKey: string;
    inputRef?: (element: HTMLInputElement | null) => void;
    className?: string;
    renderExtra?: (match: LookupMatch<T>) => ReactNode;
    maxSuggestions?: number;
    // Opt-in (klantkeuze): Tab en Enter nemen altijd de gemarkeerde suggestie over (standaard de bovenste) en gaan naar het
    // volgende veld; zonder deze vlag neemt Tab alleen een duidelijke suggestie over.
    takeHighlighted?: boolean;
    // De getypte zoektekst (bijvoorbeeld om bij geen treffer "Nieuwe klant" met die naam aan te bieden).
    onQueryChange?: (query: string) => void;
    // Vaste naam voor de rondleiding (data-tour) op het invoerveld zelf.
    dataTour?: string;
    // Onderscheidt hetzelfde veld per regel voor de rondleiding (data-tour-key).
    dataTourKey?: string | number;
}) {
    const { t } = useTranslation();
    const listId = useId();
    const inputRef = useRef<HTMLInputElement | null>(null);
    const [query, setQuery] = useState(freeText);
    const [open, setOpen] = useState(false);
    const [highlight, setHighlight] = useState(0);
    const [navigated, setNavigated] = useState(false);
    const focusAfterClear = useRef(false);

    // Vrije tekst van buiten (hersteld concept) in het veld zetten zolang er niets gekozen is.
    useEffect(() => { if (allowFreeText && selected === null) setQuery(freeText); }, [freeText, allowFreeText, selected]);

    const matches = useMemo(() => (selected === null ? search(query) : []), [search, query, selected]);
    const shown = matches.slice(0, maxSuggestions);
    const clear = unambiguousLookup(matches);

    // De lijst staat vast ten opzichte van het venster (tabellen met overflow zouden haar afsnijden) en opent naar boven
    // bij weinig ruimte; bij scrollen of vergroten wordt opnieuw gemeten.
    const [listStyle, setListStyle] = useState<CSSProperties>({});
    const listOpen = open && selected === null && query.trim() !== '';
    useLayoutEffect(() => {
        if (!listOpen) return;
        const measure = () => {
            const rect = inputRef.current?.getBoundingClientRect();
            if (!rect) return;
            const width = Math.min(Math.max(rect.width, 320), window.innerWidth - 16);
            const left = Math.max(Math.min(rect.left, window.innerWidth - width - 8), 8);
            const below = window.innerHeight - rect.bottom - 12;
            const above = rect.top - 12;
            const upwards = below < 180 && above > below;
            setListStyle(upwards
                ? { position: 'fixed', bottom: window.innerHeight - rect.top + 4, left, width, maxHeight: Math.min(Math.max(above, 120), 288) }
                : { position: 'fixed', top: rect.bottom + 4, left, width, maxHeight: Math.min(Math.max(below, 120), 288) });
        };
        measure();
        window.addEventListener('resize', measure);
        window.addEventListener('scroll', measure, true);
        return () => { window.removeEventListener('resize', measure); window.removeEventListener('scroll', measure, true); };
    }, [listOpen]);

    useEffect(() => {
        if (focusAfterClear.current) {
            focusAfterClear.current = false;
            inputRef.current?.focus();
        }
    }, [selected]);

    const changeQuery = (next: string) => {
        setQuery(next);
        onQueryChange?.(next);
    };

    const choose = (match: LookupMatch<T>) => {
        onSelect(match.item);
        changeQuery('');
        setOpen(false);
        setNavigated(false);
    };

    const clearSelection = () => {
        if (disabled) return;
        focusAfterClear.current = true;
        onClear();
        changeQuery('');
        setHighlight(0);
        setNavigated(false);
    };

    const leaveWithFreeText = () => {
        setOpen(false);
        if (allowFreeText) onFreeText?.(query);
    };

    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (selected !== null) {
            if (event.key === 'Backspace' || event.key === 'Delete') {
                event.preventDefault();
                clearSelection();
            }
            return;
        }
        switch (event.key) {
            case 'ArrowDown':
                if (shown.length === 0) return;
                event.preventDefault();
                setOpen(true);
                setNavigated(true);
                setHighlight((current) => (open ? Math.min(current + 1, shown.length - 1) : current));
                return;
            case 'ArrowUp':
                if (shown.length === 0) return;
                event.preventDefault();
                setNavigated(true);
                setHighlight((current) => Math.max(current - 1, 0));
                return;
            case 'Enter':
                // Alleen een geopende suggestie kiezen; nooit het formulier versturen vanuit het zoekveld.
                if (listOpen && shown[highlightIndex]) {
                    event.preventDefault();
                    choose(shown[highlightIndex]);
                    if (takeHighlighted) focusNextAfter(event.currentTarget);
                }
                return;
            case 'Tab': {
                // Nooit preventDefault: Tab verlaat het veld altijd. Alleen een duidelijke suggestie wordt daarbij overgenomen.
                const accepted = !listOpen ? null : takeHighlighted ? shown[highlightIndex] ?? null : navigated ? shown[highlight] ?? null : clear;
                if (accepted) choose(accepted);
                else leaveWithFreeText();
                return;
            }
            case 'Escape':
                if (open) {
                    event.preventDefault();
                    event.stopPropagation();
                    setOpen(false);
                }
                return;
            default:
                return;
        }
    };

    const highlightIndex = (() => {
        if (shown.length === 0) return -1;
        if (navigated) return Math.min(highlight, shown.length - 1);
        const clearIndex = clear ? shown.findIndex((match) => match.key === clear.key) : -1;
        return clearIndex >= 0 ? clearIndex : 0;
    })();
    const optionId = (index: number) => `${listId}-option-${index}`;
    const describedIds = [describedBy, selected ? undefined : `${listId}-hint`].filter(Boolean).join(' ') || undefined;

    return (
        <div className={`relative ${className}`}>
            <div className="flex items-start gap-1">
                <input
                    ref={(element) => { inputRef.current = element; externalRef?.(element); }}
                    id={`${id}-input`}
                    data-tour={dataTour}
                    data-tour-key={dataTourKey}
                    type="text"
                    role="combobox"
                    aria-label={ariaLabel}
                    aria-autocomplete="list"
                    aria-expanded={listOpen}
                    aria-controls={listId}
                    aria-activedescendant={listOpen && highlightIndex >= 0 ? optionId(highlightIndex) : undefined}
                    aria-invalid={invalid}
                    aria-describedby={describedIds}
                    autoComplete="off"
                    readOnly={selected !== null}
                    disabled={disabled}
                    value={selected ? selected.description : query}
                    placeholder={t(placeholderKey)}
                    title={selected ? selected.description : undefined}
                    onChange={(event) => { changeQuery(event.target.value); setOpen(true); setHighlight(0); setNavigated(false); }}
                    onFocus={() => { if (selected === null && query !== '') setOpen(true); }}
                    onBlur={leaveWithFreeText}
                    onKeyDown={onKeyDown}
                    className={`${inputClass} ${selected ? 'bg-blue-50 font-medium text-gray-900' : ''}`}
                />
                {/* Buiten de Tab-volgorde: Tab gaat na een keuze naar het volgende veld; wissen kan met Backspace of Delete. */}
                {selected !== null && (
                    <button type="button" tabIndex={-1} onClick={clearSelection} disabled={disabled} aria-label={t(changeKey)} title={t(changeKey)}
                        className="mt-1 shrink-0 rounded border border-gray-300 px-2 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-40">
                        ✕
                    </button>
                )}
            </div>
            {selected === null && <span id={`${listId}-hint`} className="sr-only">{t(hintKey)}</span>}
            {listOpen && (
                <ul id={listId} role="listbox" aria-label={ariaLabel} style={listStyle}
                    className="z-30 overflow-y-auto rounded border border-gray-300 bg-white py-1 text-sm shadow-lg">
                    {shown.length === 0 && (
                        <li role="option" aria-selected={false} aria-disabled="true" className="px-3 py-1.5 text-gray-600">{t(noneKey, { query: query.trim() })}</li>
                    )}
                    {shown.map((match, index) => {
                        const active = index === highlightIndex;
                        return (
                            <li key={match.key} id={optionId(index)} role="option" aria-selected={active}
                                onMouseDown={(event) => event.preventDefault()}
                                onClick={() => choose(match)}
                                onMouseEnter={() => { setHighlight(index); setNavigated(true); }}
                                className={`flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 ${active ? 'bg-blue-100 text-blue-900' : 'text-gray-900 hover:bg-gray-50'}`}>
                                <span>{match.description}</span>
                                {renderExtra ? renderExtra(match) : match.badge ? (
                                    <span className={`shrink-0 rounded border px-1.5 py-0.5 font-mono text-xs ${match.badgeStrong ? 'border-blue-400 bg-white text-blue-900' : 'border-gray-300 bg-gray-50 text-gray-700'}`}>
                                        {match.badge}
                                    </span>
                                ) : null}
                            </li>
                        );
                    })}
                    {matches.length > shown.length && (
                        <li role="option" aria-selected={false} aria-disabled="true" className="px-3 py-1.5 text-xs text-gray-500">{t(moreKey, { count: String(matches.length - shown.length) })}</li>
                    )}
                </ul>
            )}
        </div>
    );
}
