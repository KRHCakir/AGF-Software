'use client';

import { KeyboardEvent, useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { exactLot, matchingLots } from '../../lib/lotOptions';
import { toApiError } from '../../services/apiClient';
import { getLotOptions } from '../../services/salesOrderService';
import type { LotOption } from '../../types/salesOrder';
import { inputClass } from '../supply/FieldError';

export interface ChosenLot { partijId: number; number: string }

// Feedback 22 november: de partijkeuze. Typen (ook zonder I-, streepjes of spaties) of de lijst openen (pijltje omlaag); per partij het vrije
// aantal, maat, kleur, herkomst, locatie en ontvangstdatum, de oudste eerst. Pijltjes kiezen, Enter neemt de partij, Escape sluit. Leeg
// laten = de oudste partij eerst (FIFO). Een houdbaarheidsdatum bestaat nog niet. Op een smal scherm staan de kenmerken onder het nummer.
export default function LotPicker({ productVariantId, unit, value, onChange, disabled = false, id, label, hint, dataTour, exclude = null, onPendingChange }: {
    productVariantId: number | null;
    unit: string;
    value: ChosenLot | null;
    onChange: (lot: ChosenLot | null) => void;
    disabled?: boolean;
    id: string;
    label: string;
    hint?: string;
    dataTour?: string;
    // Een partij die niet gekozen kan worden (bijvoorbeeld de huidige partij bij "Partij wijzigen").
    exclude?: number | null;
    // Meldt of er tekst staat die (nog) niet als partij is vastgelegd, zodat het formulier opslaan kan blokkeren.
    onPendingChange?: (pending: boolean) => void;
}) {
    const { t, locale } = useTranslation();
    const [text, setText] = useState(value?.number ?? '');
    const [options, setOptions] = useState<LotOption[] | null>(null);
    const [failed, setFailed] = useState(false);
    const [open, setOpen] = useState(false);
    const [active, setActive] = useState(0);
    const listId = useId();
    const input = useRef<HTMLInputElement>(null);

    // Het nummer zoals de partijkeuze het kent (een bevroren opdracht weet alleen het id).
    const known = value === null ? null : options?.find((option) => option.partijId === value.partijId)?.number ?? value.number;
    // Wissen we de keuze zelf omdat de tekst afwijkt, dan blijft de getypte tekst staan.
    const keepText = useRef(false);
    useEffect(() => {
        if (keepText.current) { keepText.current = false; return; }
        setText(known ?? '');
    }, [value?.partijId, known, productVariantId]); // een ander artikel wist ook niet-vastgelegde tekst

    // De opties van dit artikel in deze eenheid, één keer per artikel; een ander artikel of andere eenheid haalt ze opnieuw op.
    useEffect(() => {
        setOptions(null);
        setFailed(false);
        if (productVariantId === null || unit === '') return;
        const controller = new AbortController();
        getLotOptions(productVariantId, unit, controller.signal)
            .then(setOptions)
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setFailed(true); });
        return () => controller.abort();
    }, [productVariantId, unit]);

    const shown = matchingLots(options ?? [], value !== null && text === known ? '' : text).filter((option) => option.partijId !== exclude);
    // Enter op een precies getypt nummer kiest die partij, ook als de lijst dicht is of nog laadt (Chrome-proef: snel typen en Enter).
    const enterPending = useRef(false);
    const takeExact = (from: readonly LotOption[]) => {
        const exact = exactLot(from, text);
        if (exact !== null && exact.partijId !== exclude && exact.partijId !== value?.partijId) choose(exact);
        else setOpen(false);
    };
    useEffect(() => {
        if (options === null || !enterPending.current) return;
        enterPending.current = false;
        takeExact(options);
    }, [options]); // eslint-disable-line react-hooks/exhaustive-deps -- alleen bij binnenkomende opties
    const choose = (option: LotOption | null) => {
        if (option === null) { onChange(null); setText(''); }
        else { onChange({ partijId: option.partijId!, number: option.number! }); setText(option.number!); }
        setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'ArrowDown') { event.preventDefault(); setOpen(true); setActive((n) => Math.min(n + (open ? 1 : 0), Math.max(0, shown.length - 1))); }
        else if (event.key === 'ArrowUp') { event.preventDefault(); setActive((n) => Math.max(0, n - 1)); }
        else if (event.key === 'Escape' && open) { event.preventDefault(); setOpen(false); }
        else if (event.key === 'Enter') {
            // Enter mag in een partijkeuze nooit het formulier verzenden.
            event.preventDefault();
            if (open && shown[active]) choose(shown[active]);
            else if (options === null && text.trim() !== '') enterPending.current = true;
            else takeExact(options ?? []);
        }
    };
    // Verlaten: precies een bestaand nummer kiest die partij, leeg haalt de keuze weg; iets anders blijft staan met een melding.
    const onBlur = () => {
        setOpen(false);
        if (text.trim() === '') { enterPending.current = false; if (value !== null) choose(null); return; }
        const exact = exactLot(options ?? [], text);
        if (exact !== null && exact.partijId !== exclude && exact.partijId !== value?.partijId) choose(exact);
    };
    const unknown = options !== null && text.trim() !== '' && (value === null || text !== known) && exactLot(options, text) === null;
    const pending = text.trim() !== '' && (value === null || text !== known);
    const report = useRef(onPendingChange);
    report.current = onPendingChange;
    useEffect(() => { report.current?.(pending); }, [pending]);
    useEffect(() => () => report.current?.(false), []);
    const location = (option: LotOption) => option.locations.map((name) => name ?? t('lot_picker_own_warehouse')).join(', ')
        + (option.bins.length > 0 ? ` (${option.bins.join(', ')})` : '');

    return (
        <div className="relative" data-tour={dataTour}>
            <label htmlFor={id} className="text-xs font-medium text-gray-700">{label}</label>
            <input ref={input} id={id} value={text} data-tour={dataTour ? `${dataTour}-input` : undefined} disabled={disabled || productVariantId === null} autoComplete="off" role="combobox" aria-expanded={open} aria-controls={listId}
                aria-autocomplete="list" aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
                onChange={(event) => { setText(event.target.value); setOpen(true); setActive(0); if (value !== null && event.target.value !== known) { keepText.current = true; onChange(null); } }}
                onFocus={() => setActive(0)} onKeyDown={onKeyDown} onBlur={onBlur} placeholder={t('lot_picker_placeholder')} className={`${inputClass} font-mono`} />
            {hint && <span className="block text-xs text-gray-500">{hint}</span>}
            {failed && <span className="block text-xs text-red-700">{t('lot_picker_failed')}</span>}
            {unknown && !open && <span className="block text-xs text-red-700" role="alert">{t('lot_picker_unknown', { number: text.trim() })}</span>}
            {open && options !== null && (
                <ul id={listId} role="listbox" className="absolute z-20 mt-1 max-h-72 w-full min-w-[18rem] overflow-y-auto rounded border border-gray-300 bg-white text-sm shadow-lg"
                    data-testid="lot-picker-list">
                    {shown.length === 0 && <li className="px-2 py-1.5 text-gray-600">{t('lot_picker_none')}</li>}
                    {shown.map((option, index) => (
                        <li key={option.partijId} id={`${listId}-${index}`} role="option" aria-selected={index === active}
                            onMouseDown={(event) => { event.preventDefault(); choose(option); }} onMouseEnter={() => setActive(index)}
                            className={`cursor-pointer px-2 py-1.5 ${index === active ? 'bg-blue-50' : ''}`} data-tour="lot-picker-option" data-tour-key={option.number ?? ''}>
                            <span className="font-mono font-semibold">{option.number}</span>
                            <span className="ml-2 tabular-nums">{t('lot_picker_free', { quantity: formatQuantity(option.availableQuantity, locale) })}</span>
                            <span className="mt-0.5 flex flex-wrap gap-x-3 text-xs text-gray-700">
                                <span>{t('lot_picker_size')}: {option.size ?? '–'}</span>
                                <span>{t('lot_picker_color')}: {option.color ?? '–'}</span>
                                <span>{t('lot_picker_origin')}: {option.origin ?? '–'}</span>
                                <span>{t('lot_picker_location')}: {location(option)}</span>
                                <span>{t('lot_picker_received')}: {formatCalendarDate(option.receivedAt, locale)}</span>
                            </span>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
