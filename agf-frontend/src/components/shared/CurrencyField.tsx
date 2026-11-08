'use client';

import { KeyboardEvent, useId, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { cleanCurrencyInput, currencyName, suggestCurrencies } from '../../lib/currencies';
import { inputClass } from '../supply/FieldError';

// Valutaveld met suggesties (feedback 8 november): typ E en EUR staat bovenaan klaar; Tab of Enter neemt de gemarkeerde suggestie
// over (Tab gaat daarna gewoon door naar het volgende veld), pijltjes kiezen een andere, Escape sluit de lijst. Een andere code van
// drie letters mag ook: de lijst is een hulp, geen verplichting. De lijst gaat pas open als je typt of op pijltje omlaag drukt, zodat
// Tab door een al ingevuld veld niets verandert. De lijst klapt naar links uit: het veld staat vaak rechts in een rij.
export default function CurrencyField({ id, value, onChange, disabled = false, className = '', dataTour, ...aria }: {
    id: string;
    value: string;
    onChange: (value: string) => void;
    disabled?: boolean;
    className?: string;
    dataTour?: string;
    'aria-invalid'?: boolean;
    'aria-describedby'?: string;
}) {
    const { t, locale } = useTranslation();
    const listId = useId();
    const [open, setOpen] = useState(false);
    const [highlight, setHighlight] = useState(0);
    const [chosen, setChosen] = useState(false); // met de pijltjes expliciet een suggestie gekozen (ook de eerste)
    const suggestions = open ? suggestCurrencies(value, locale) : [];
    const active = suggestions[Math.min(highlight, suggestions.length - 1)] ?? null;
    // Een suggestie die alleen op naam matcht (ALL bij 'dollar') mag een ingetypte code niet stilletjes vervangen,
    // tenzij de gebruiker er met de pijltjes expliciet naartoe is gegaan, of er nog geen volledige code van drie letters staat.
    const mayTake = active !== null && (active.code.startsWith(value) || chosen || value.length < 3);

    const choose = (code: string) => {
        onChange(code);
        setOpen(false);
    };

    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            setChosen(true);
            if (!open) {
                setOpen(true);
                setHighlight(0);
                return;
            }
            const count = suggestions.length;
            if (count > 0) setHighlight((current) => (event.key === 'ArrowDown' ? (current + 1) % count : (current - 1 + count) % count));
            return;
        }
        if (!open || active === null) return;
        if (event.key === 'Enter') {
            if (mayTake) {
                event.preventDefault();
                choose(active.code);
            } else setOpen(false);
        } else if (event.key === 'Tab') {
            if (mayTake) choose(active.code);
            else setOpen(false);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            setOpen(false);
        }
    };

    return (
        <div className={`relative ${className}`}>
            <input id={id} type="text" value={value} disabled={disabled} maxLength={3} autoComplete="off" spellCheck={false}
                role="combobox" aria-autocomplete="list" aria-expanded={open && suggestions.length > 0} aria-controls={listId}
                aria-activedescendant={open && active !== null ? `${listId}-${active.code}` : undefined} data-tour={dataTour} {...aria}
                onChange={(event) => {
                    const next = cleanCurrencyInput(event.target.value);
                    onChange(next);
                    setOpen(next !== '');
                    setHighlight(0);
                    setChosen(false);
                }}
                onKeyDown={onKeyDown} onBlur={() => setOpen(false)} className={`${inputClass} font-mono uppercase`} />
            <ul id={listId} role="listbox" aria-label={t('currency_suggestions')} hidden={!open || suggestions.length === 0}
                className="absolute right-0 top-full z-30 mt-0.5 max-h-64 w-max min-w-full max-w-[16rem] overflow-y-auto rounded border border-gray-300 bg-white py-1 text-sm shadow-lg">
                {suggestions.map((option) => (
                    <li key={option.code} id={`${listId}-${option.code}`} role="option" aria-selected={option === active}
                        onMouseDown={(event) => event.preventDefault()} onClick={() => choose(option.code)}
                        className={`flex cursor-pointer gap-2 px-2 py-1 ${option === active ? 'bg-blue-100 text-blue-900' : 'text-gray-800 hover:bg-gray-100'}`}>
                        <span className="font-mono font-semibold">{option.code}</span>
                        <span className="text-gray-600">{currencyName(option, locale)}</span>
                    </li>
                ))}
            </ul>
            {open && active !== null && <span className="sr-only" aria-live="polite">{t(mayTake ? 'currency_suggestion_hint' : 'currency_suggestion_hint_name', { code: active.code })}</span>}
        </div>
    );
}
