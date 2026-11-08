'use client';

import { RefObject, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { dateShortcut, formatDateInput, isDateOnly, localToday, parseDateInput } from '../../lib/calendarDate';
import { inputClass } from '../supply/FieldError';

// Het datumveld van de hele app. Een gewoon tekstveld in plaats van <input type="date">: dat native veld toont de datum in de
// volgorde van de browsertaal (en-US: mm/dd/jjjj), laat het typen beginnen in het vakje waar je klikt en neemt in het jaarvakje tot
// zes cijfers aan, terwijl een smal veld de rest afknipt. Zo stond er "11/02/2026" terwijl het veld leeg (onvolledig) was en de
// controle terecht "Vul een geldige datum in" zei. Hier staat altijd dd-mm-jjjj, ongeacht de browsertaal; typen mag als dd-mm-jjjj,
// dd/mm/jjjj, d-m-jjjj, ddmmjjjj, dd-mm-jj (20jj), d-m (dit jaar) of ISO. "Vandaag" vult de lokale kalenderdag in (nooit UTC); de
// kalenderknop opent de kiezer van de browser als die er is.
//
// `value` en `onChange` gaan over de ISO-datum (jjjj-mm-dd) zonder tijd en zonder tijdzone; wat (nog) geen datum is, gaat als de
// getypte tekst door, zodat het formulier "ongeldige datum" kan melden in plaats van "leeg". Eigen getypte tekst blijft staan
// tot het verlaten van het veld of Enter; daarna, en bij elke waarde van buiten (Vandaag, kalender, herstel, leegmaken), staat er dd-mm-jjjj.
// Sneltoetsen: een losse letter wordt een datum zodra je het veld verlaat of Enter drukt, zodat "morgen" voluit te typen blijft (V vandaag, G gisteren, M morgen, E eergisteren, O overmorgen; zie dateShortcut). Zolang
// het veld de focus heeft, staat de uitleg eronder (zwevend, zodat het formulier niet verspringt).
export default function DateField({ id, value, onChange, disabled = false, min, max, dataTour, dataTourKey, inputRef, ariaLabel, className = '', ...aria }: {
    id: string;
    value: string;
    onChange: (value: string) => void;
    disabled?: boolean;
    min?: string;
    max?: string;
    dataTour?: string;
    dataTourKey?: string | number;
    inputRef?: RefObject<HTMLInputElement | null>;
    ariaLabel?: string;
    className?: string;
    'aria-invalid'?: boolean;
    'aria-describedby'?: string;
}) {
    const { t } = useTranslation();
    const [text, setText] = useState(() => formatDateInput(value));
    const sent = useRef(value);
    const picker = useRef<HTMLInputElement>(null);
    const [canPick, setCanPick] = useState(false);
    const [focused, setFocused] = useState(false);
    const hintId = `${id}-shortcuts`;
    const describedBy = [aria['aria-describedby'], hintId].filter(Boolean).join(' ');

    useEffect(() => {
        if (value === sent.current) return;
        sent.current = value;
        setText(formatDateInput(value));
    }, [value]);

    useEffect(() => {
        setCanPick(typeof window.HTMLInputElement.prototype.showPicker === 'function');
    }, []);

    const emit = (next: string) => {
        sent.current = next;
        onChange(next);
    };

    const type = (raw: string) => {
        setText(raw);
        emit(parseDateInput(raw) ?? raw.trim());
    };

    const choose = (iso: string) => {
        setText(formatDateInput(iso));
        emit(iso);
    };

    const settle = () => {
        const shortcut = dateShortcut(text);
        if (shortcut !== null) {
            choose(shortcut);
            return;
        }
        const iso = parseDateInput(text);
        if (iso !== null) setText(formatDateInput(iso));
    };

    const openPicker = () => {
        try { picker.current?.showPicker(); } catch { /* geen kiezer (bijv. zonder gebruikersactie): typen blijft werken */ }
    };

    const smallButton = 'mt-1 min-h-[1.875rem] shrink-0 rounded border border-gray-300 bg-white px-2 text-xs font-medium text-gray-800 hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-blue-500';

    return (
        <div className={`relative flex flex-wrap items-stretch gap-x-1 ${className}`}>
            <input id={id} ref={inputRef} type="text" inputMode="text" autoComplete="off" spellCheck={false} value={text} disabled={disabled}
                placeholder={t('date_field_placeholder')} aria-label={ariaLabel} data-tour={dataTour} data-tour-key={dataTourKey} {...aria} aria-describedby={describedBy}
                onChange={(event) => type(event.target.value)} onFocus={() => setFocused(true)} onBlur={() => { setFocused(false); settle(); }} onKeyDown={(event) => { if (event.key === 'Enter') settle(); }} className={`${inputClass} min-w-0 flex-1 basis-[7.5rem] tabular-nums`} />
            <span id={hintId} data-testid="date-field-shortcuts" className={focused
                ? 'pointer-events-none absolute left-0 top-full z-30 mt-0.5 whitespace-nowrap rounded border border-gray-200 bg-white px-1.5 py-0.5 text-xs text-gray-600 shadow-sm'
                : 'sr-only'}>{t('date_field_shortcuts')}</span>
            <button type="button" className={smallButton} disabled={disabled} onClick={() => choose(localToday())}
                aria-label={t('date_field_today_label')}>{t('date_field_today')}</button>
            {canPick && (
                <>
                    <button type="button" className={smallButton} disabled={disabled} onClick={openPicker} aria-label={t('date_field_calendar')}>
                        <span aria-hidden="true">📅</span>
                    </button>
                    <input ref={picker} type="date" tabIndex={-1} aria-hidden="true" value={isDateOnly(value) ? value : ''} min={min} max={max}
                        onChange={(event) => { if (isDateOnly(event.target.value)) choose(event.target.value); }}
                        className="pointer-events-none absolute bottom-0 right-0 h-px w-px opacity-0" />
                </>
            )}
        </div>
    );
}
