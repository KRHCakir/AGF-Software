'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_PARTIJ_NUMBER_LENGTH, partijNumberKey } from '../../lib/partijRules';
import { findPartijByNumber, listPartijen } from '../../services/partijService';
import type { PartijSummary, PartijTotals } from '../../types/partij';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Wat het partijveld van de inkooporder voorstelt: het voorgestelde nummer (de server kent het echte toe bij opslaan), een eigen nummer
// (na de dubbelcontrole), een bestaande partij (tweede order op dezelfde lading) of niets.
export type PartijChoice =
    | { kind: 'suggested'; number: string }
    | { kind: 'own'; number: string }
    | { kind: 'existing'; number: string; partij: PartijSummary | PartijTotals }
    | { kind: 'none'; number: '' };

export const NO_PARTIJ: PartijChoice = { kind: 'none', number: '' };

// Wat er naar de server gaat: een bestaande partij, een nieuwe (leeg nummer = het volgende voorstel) of niets.
export function partijPayload(choice: PartijChoice): { partijId: number } | { newPartij: { number: string | null } } | Record<string, never> {
    if (choice.kind === 'existing') return { partijId: choice.partij.id };
    if (choice.kind === 'suggested') return { newPartij: { number: null } };
    if (choice.kind === 'own') return { newPartij: { number: choice.number.trim() } };
    return {};
}

// Getypte tekst: gelijk aan het voorstel is het voorstel, leeg is geen partij, anders een eigen nummer.
export function choiceFromText(text: string, suggestion: string | null): PartijChoice {
    if (text.trim() === '') return NO_PARTIJ;
    if (suggestion !== null && text === suggestion) return { kind: 'suggested', number: text };
    return { kind: 'own', number: text };
}

// Veld Partij bovenaan de inkooporder, met "Bestaande partij kiezen" (zoeken op nummer, product, leverancier) en de dubbelcontrole: bestaat
// het getypte nummer al (zelfde sleutel), dan vraagt het scherm of die partij gebruikt moet worden. Nooit stilletjes een tweede partij.
export default function PartijField({ value, suggestion, exists, disabled, errors, onChange, onExists }: {
    value: PartijChoice;
    suggestion: string | null;
    // De partij die al bestaat met het getypte nummer (na het verlaten van het veld of na 409 bij opslaan); null = geen vraag open.
    exists: PartijSummary | null;
    disabled: boolean;
    errors?: string[];
    onChange: (choice: PartijChoice) => void;
    onExists: (partij: PartijSummary | null) => void;
}) {
    const { t } = useTranslation();
    const input = useRef<HTMLInputElement>(null);
    const [picking, setPicking] = useState(false);
    const [search, setSearch] = useState('');
    const [found, setFound] = useState<PartijTotals[] | null>(null);

    const [searchFailed, setSearchFailed] = useState(false);
    const [retry, setRetry] = useState(0);
    const checkController = useRef<AbortController | null>(null);

    useEffect(() => {
        if (!picking) return;
        const controller = new AbortController();
        const timer = window.setTimeout(() => {
            setSearchFailed(false);
            listPartijen({ query: search, status: 'all', page: 1, pageSize: 10 }, controller.signal)
                .then((list) => { if (!controller.signal.aborted) setFound(list.items); })
                .catch(() => {
                    if (controller.signal.aborted) return;
                    setFound([]);
                    setSearchFailed(true);
                });
        }, search.trim() === '' ? 0 : 150);
        return () => { window.clearTimeout(timer); controller.abort(); };
    }, [picking, search, retry]);

    // Een lopende dubbelcontrole vervalt zodra de gebruiker het nummer wijzigt, een partij kiest of het veld verlaat.
    useEffect(() => () => checkController.current?.abort(), []);

    // Dubbelcontrole bij het verlaten van het veld; het voorstel zelf is per definitie vrij (de server kiest bij botsing het volgende).
    const check = async () => {
        checkController.current?.abort();
        if (value.kind !== 'own' || partijNumberKey(value.number) === null) return;
        const controller = new AbortController();
        checkController.current = controller;
        try {
            const existing = await findPartijByNumber(value.number, controller.signal);
            if (controller.signal.aborted) return;
            if (existing !== null) onExists(existing);
        } catch {
            // Geen antwoord: de server controleert bij opslaan opnieuw en geeft dan dezelfde vraag.
        }
    };

    const chooseExisting = (partij: PartijSummary | PartijTotals) => {
        checkController.current?.abort();
        onExists(null);
        setPicking(false);
        onChange({ kind: 'existing', number: partij.number, partij });
    };

    const otherNumber = () => {
        onExists(null);
        window.setTimeout(() => { input.current?.focus(); input.current?.select(); }, 0);
    };

    return (
        <div className="space-y-1" data-tour="supply-partij-block">
            <label htmlFor="partij-input" className="text-xs font-medium text-gray-700">{t('partij_field')}</label>
            <div className="flex flex-wrap items-center gap-2">
                <input {...fieldProps('partij', errors)} ref={input} data-tour="supply-partij" value={value.number} maxLength={MAX_PARTIJ_NUMBER_LENGTH}
                    disabled={disabled || value.kind === 'existing'} aria-describedby="partij-hint"
                    onChange={(event) => { checkController.current?.abort(); onExists(null); onChange(choiceFromText(event.target.value, suggestion)); }}
                    onBlur={() => { void check(); }} className={`${inputClass} !w-40 font-mono`} />
                {value.kind === 'existing' ? (
                    <button type="button" className={secondaryButtonClass} disabled={disabled} data-tour="supply-partij-clear"
                        onClick={() => onChange(suggestion === null ? NO_PARTIJ : { kind: 'suggested', number: suggestion })}>{t('partij_other')}</button>
                ) : (
                    <button type="button" className={secondaryButtonClass} disabled={disabled} data-tour="supply-partij-choose" aria-expanded={picking}
                        onClick={() => { setPicking((open) => !open); setFound(null); }}>{t('partij_choose_existing')}</button>
                )}
            </div>
            <span id="partij-hint" className="block text-xs text-gray-500">
                {value.kind === 'suggested' ? t('partij_hint_suggested')
                    : value.kind === 'existing' ? t('partij_hint_existing', { vendors: 'vendors' in value.partij ? value.partij.vendors.join(', ') || '–' : '–' })
                        : value.kind === 'own' ? t('partij_hint_own') : t('partij_hint_none')}
            </span>
            <FieldError id="partij" messages={errors} />
            {exists !== null && (
                <div role="alertdialog" aria-labelledby="partij-exists-text" className="rounded border border-amber-300 bg-amber-50 p-2 text-sm text-amber-950" data-tour="supply-partij-exists">
                    <p id="partij-exists-text">{t('partij_exists_question', { number: exists.number })}</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" className={secondaryButtonClass} data-tour="supply-partij-use-existing" onClick={() => chooseExisting(exists)}>{t('partij_use_existing')}</button>
                        <button type="button" className={secondaryButtonClass} data-tour="supply-partij-other-number" onClick={otherNumber}>{t('partij_other_number')}</button>
                    </div>
                </div>
            )}
            {picking && (
                <div className="rounded border border-gray-300 bg-gray-50 p-2" data-tour="supply-partij-picker">
                    <label htmlFor="partij-search-input" className="text-xs font-medium text-gray-700">{t('partij_search')}</label>
                    <input id="partij-search-input" autoFocus value={search} onChange={(event) => setSearch(event.target.value)} className={inputClass}
                        onKeyDown={(event) => { if (event.key === 'Escape') setPicking(false); }} />
                    {found === null ? <p className="mt-1 text-xs text-gray-600">{t('supply_open_loading')}</p>
                        : searchFailed ? (
                            <div role="alert" className="mt-1 text-xs text-red-700">
                                <p>{t('partij_search_failed')}</p>
                                <button type="button" className={secondaryButtonClass} onClick={() => { setFound(null); setRetry((n) => n + 1); }}>{t('partij_search_retry')}</button>
                            </div>
                        ) : found.length === 0 ? <p className="mt-1 text-xs text-gray-600">{t('partij_search_none')}</p>
                            : (
                                <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white">
                                    {found.map((partij) => (
                                        <li key={partij.id}>
                                            <button type="button" className="block w-full px-2 py-1 text-left text-sm hover:bg-blue-50 focus:bg-blue-50" data-tour="supply-partij-option" data-tour-key={partij.number}
                                                onClick={() => chooseExisting(partij)}>
                                                <span className="font-mono font-semibold">{partij.number}</span>
                                                <span className="text-gray-700"> · {partij.products.join(', ') || '–'} · {partij.vendors.join(', ') || '–'}</span>
                                            </button>
                                        </li>
                                    ))}
                                </ul>
                            )}
                </div>
            )}
        </div>
    );
}
