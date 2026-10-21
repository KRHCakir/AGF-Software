'use client';

import { CSSProperties, KeyboardEvent, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_SUGGESTIONS, findVariant, searchVariants, unambiguousMatch } from '../../lib/productSearch';
import type { SearchableProduct, VariantMatch } from '../../lib/productSearch';
import { inputClass } from '../supply/FieldError';

// Gedeeld doorzoekbaar productveld voor Aanvoer plannen en Nieuwe verkooporder. Zoekt op product, variant, verpakking,
// herkomst, maatklasse en de bestaande snelcode (ShortcutCode). Toetsenbord: pijltjes navigeren, Enter kiest, Escape
// sluit de suggesties. Tab accepteert alleen een duidelijke suggestie (één treffer, exacte snelcode, of een met de pijltjes
// gemarkeerde regel) en verlaat het veld daarna gewoon; zonder duidelijke keuze verlaat Tab het veld zonder te kiezen.
// Een snelcode kiest uitsluitend een variant; hoeveelheid of gewicht wordt hier nooit ingevuld.

export default function ProductVariantPicker({ id, products, value, onChange, disabled = false, invalid = false, describedBy, ariaLabel }: {
    // Het invoerveld krijgt `${id}-input`, zoals fieldProps() dat voor labels en foutmeldingen doet.
    id: string;
    products: readonly SearchableProduct[];
    value: string;
    onChange: (variantId: string) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    ariaLabel: string;
}) {
    const { t } = useTranslation();
    const listId = useId();
    const inputRef = useRef<HTMLInputElement>(null);
    const [query, setQuery] = useState('');
    const [open, setOpen] = useState(false);
    const [highlight, setHighlight] = useState(0);
    // Pas na pijltjesnavigatie is de gemarkeerde regel een bewuste keuze die Tab mag accepteren.
    const [navigated, setNavigated] = useState(false);
    const focusAfterClear = useRef(false);

    const selected = useMemo(() => (value === '' ? null : findVariant(products, Number(value))), [products, value]);
    const matches = useMemo(() => (selected === null ? searchVariants(products, query) : []), [products, query, selected]);
    const shown = matches.slice(0, MAX_SUGGESTIONS);
    const clear = unambiguousMatch(matches);

    // De suggesties staan vast ten opzichte van het venster: de formuliertabel heeft overflow-x-auto en zou een gewone
    // absolute lijst afsnijden (smal scherm). Bij scrollen of vergroten (ook het schermtoetsenbord) wordt opnieuw gemeten.
    const [listStyle, setListStyle] = useState<CSSProperties>({});
    const listOpen = open && selected === null && query.trim() !== '';
    useLayoutEffect(() => {
        if (!listOpen) return;
        const measure = () => {
            const rect = inputRef.current?.getBoundingClientRect();
            if (!rect) return;
            const width = Math.min(Math.max(rect.width, 320), window.innerWidth - 16);
            const left = Math.max(Math.min(rect.left, window.innerWidth - width - 8), 8);
            // Onder het veld, tenzij daar te weinig ruimte is en boven meer; dan opent de lijst naar boven.
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

    const choose = (match: VariantMatch) => {
        onChange(String(match.variantId));
        setQuery('');
        setOpen(false);
        setNavigated(false);
    };

    const clearSelection = () => {
        if (disabled) return;
        focusAfterClear.current = true;
        onChange('');
        setQuery('');
        setHighlight(0);
        setNavigated(false);
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
                if (open && shown[highlight]) {
                    event.preventDefault();
                    choose(shown[highlight]);
                }
                return;
            case 'Tab': {
                // Nooit preventDefault: Tab verlaat het veld altijd. Alleen een duidelijke suggestie wordt daarbij overgenomen.
                const accepted = open ? (navigated ? shown[highlight] ?? null : clear) : null;
                if (accepted) choose(accepted);
                else setOpen(false);
                return;
            }
            case 'Escape':
                if (open) {
                    event.preventDefault();
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
        const clearIndex = clear ? shown.findIndex((match) => match.variantId === clear.variantId) : -1;
        return clearIndex >= 0 ? clearIndex : 0;
    })();
    const optionId = (index: number) => `${listId}-option-${index}`;
    const describedIds = [describedBy, selected ? undefined : `${listId}-hint`].filter(Boolean).join(' ') || undefined;

    return (
        <div className="relative">
            <div className="flex items-start gap-1">
                <input
                    ref={inputRef}
                    id={`${id}-input`}
                    type="text"
                    role="combobox"
                    aria-label={ariaLabel}
                    aria-autocomplete="list"
                    aria-expanded={open && selected === null}
                    aria-controls={listId}
                    aria-activedescendant={open && highlightIndex >= 0 ? optionId(highlightIndex) : undefined}
                    aria-invalid={invalid}
                    aria-describedby={describedIds}
                    autoComplete="off"
                    readOnly={selected !== null}
                    disabled={disabled}
                    value={selected ? selected.description : query}
                    placeholder={t('product_picker_placeholder')}
                    title={selected ? selected.description : undefined}
                    onChange={(event) => { setQuery(event.target.value); setOpen(true); setHighlight(0); setNavigated(false); }}
                    onFocus={() => { if (selected === null && query !== '') setOpen(true); }}
                    onBlur={() => setOpen(false)}
                    onKeyDown={onKeyDown}
                    className={`${inputClass} ${selected ? 'bg-blue-50 font-medium text-gray-900' : ''}`}
                />
                {/* Buiten de Tab-volgorde: Tab gaat na een keuze naar het volgende veld; wissen kan met Backspace of Delete. */}
                {selected !== null && (
                    <button type="button" tabIndex={-1} onClick={clearSelection} disabled={disabled} aria-label={t('product_picker_change')} title={t('product_picker_change')}
                        className="mt-1 shrink-0 rounded border border-gray-300 px-2 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-40">
                        ✕
                    </button>
                )}
            </div>
            {selected === null && <span id={`${listId}-hint`} className="sr-only">{t('product_picker_hint')}</span>}
            {listOpen && (
                <ul id={listId} role="listbox" aria-label={ariaLabel} style={listStyle}
                    className="z-30 overflow-y-auto rounded border border-gray-300 bg-white py-1 text-sm shadow-lg">
                    {shown.length === 0 && (
                        <li role="option" aria-selected={false} aria-disabled="true" className="px-3 py-1.5 text-gray-600">{t('product_picker_none', { query: query.trim() })}</li>
                    )}
                    {shown.map((match, index) => {
                        const active = index === highlightIndex;
                        return (
                            <li key={match.variantId} id={optionId(index)} role="option" aria-selected={active}
                                onMouseDown={(event) => event.preventDefault()}
                                onClick={() => choose(match)}
                                onMouseEnter={() => { setHighlight(index); setNavigated(true); }}
                                className={`flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 ${active ? 'bg-blue-100 text-blue-900' : 'text-gray-900 hover:bg-gray-50'}`}>
                                <span>{match.description}</span>
                                {match.variant.shortcutCode !== null && (
                                    <span className={`shrink-0 rounded border px-1.5 py-0.5 font-mono text-xs ${match.exactShortcut ? 'border-blue-400 bg-white text-blue-900' : 'border-gray-300 bg-gray-50 text-gray-700'}`}>
                                        {match.variant.shortcutCode}
                                    </span>
                                )}
                            </li>
                        );
                    })}
                    {matches.length > shown.length && (
                        <li role="option" aria-selected={false} aria-disabled="true" className="px-3 py-1.5 text-xs text-gray-500">{t('product_picker_more', { count: String(matches.length - shown.length) })}</li>
                    )}
                </ul>
            )}
        </div>
    );
}
