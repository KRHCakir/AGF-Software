'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly, isDateOnly } from '../../lib/calendarDate';
import { type CurrencyDraft, effectiveRate, inverseLine, rateLine, rateText } from '../../lib/documentCurrency';
import { EURO, isEuro } from '../../lib/exchangeRate';
import { toApiError } from '../../services/apiClient';
import { getExchangeRate } from '../../services/purchaseInvoiceService';
import type { PurchaseCurrency } from '../../services/purchaseCurrencyService';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';

type Fetch = { status: 'idle' | 'loading' | 'ready' } | { status: 'none' | 'unavailable' };

// Valuta-ronde: valuta en koers bovenaan een inkooporder (en later de factuur). Euro is standaard. Kies je een andere valuta, dan haalt
// het scherm de ECB-koers voor de documentdatum op en vult hem in ("1 EUR = 1,0842 USD · ECB, 18-11-2026", met de andere richting als
// uitleg). Pas je de koers aan, dan staat er "Handmatig" en vraagt het scherm daarna nergens meer om bevestiging. Geen ECB-koers of de ECB
// niet bereikbaar: dat staat er, en je vult de koers zelf in. `fetchOnOpen` = false bij aanpassen: de vastgelegde koers blijft staan tot
// je een andere valuta kiest.
export default function DocumentCurrencyField({ value, onChange, documentDate, currencies, errors, disabled = false, fetchOnOpen = true, prefix = 'supply' }: {
    value: CurrencyDraft;
    onChange: (next: CurrencyDraft) => void;
    documentDate: string;
    currencies: PurchaseCurrency[];
    errors: FieldErrors;
    disabled?: boolean;
    fetchOnOpen?: boolean;
    prefix?: string;
}) {
    const { t, locale } = useTranslation();
    const [fetch, setFetch] = useState<Fetch>({ status: 'idle' });
    const code = value.currency.trim().toUpperCase();
    const foreign = /^[A-Z]{3}$/.test(code) && !isEuro(code);
    const date = isDateOnly(documentDate) ? documentDate : null;
    // De ECB-koers volgen zolang de bron ECB is: bij een andere valuta of documentdatum opnieuw ophalen. Bij aanpassen van een opgeslagen
    // document met een ECB-koers blijft die vastgelegde koers staan zolang valuta en bron dezelfde zijn, ook bij een andere datum: zo houdt
    // de server hem ook (DocumentCurrency.SameChoiceAs). Een opgeslagen euro- of handmatige keuze legt niets vast: een andere valuta haalt op.
    const stored = useRef(!fetchOnOpen && foreign && value.source === 'ECB' && value.rate !== '' ? { currency: code, rate: value.rate, rateDate: value.rateDate } : null);
    const latest = useRef(value);
    latest.current = value;

    useEffect(() => {
        if (!foreign) { setFetch({ status: 'idle' }); return; }
        // Handmatig: niets ophalen; een melding "geen ECB-koers" of "niet bereikbaar" blijft staan, een lopende laadmelding niet (de aanvraag
        // is afgebroken).
        if (value.source === 'Manual') { setFetch((current) => (current.status === 'loading' ? { status: 'idle' } : current)); return; }
        const keep = stored.current;
        if (keep !== null && keep.currency === code) {
            setFetch({ status: 'idle' });
            if (latest.current.rate !== keep.rate || latest.current.rateDate !== keep.rateDate) onChange({ ...latest.current, rate: keep.rate, rateDate: keep.rateDate });
            return;
        }
        const controller = new AbortController();
        setFetch({ status: 'loading' });
        // De vorige koers geldt niet meer (andere valuta of datum): leeg tot de nieuwe binnen is, zodat er geen eurobedrag van de oude koers
        // staat en opslaan wacht.
        if (latest.current.rate !== '' || latest.current.rateDate !== null) onChange({ ...latest.current, rate: '', rateDate: null });
        getExchangeRate(code, date, controller.signal)
            .then((rate) => {
                setFetch({ status: 'ready' });
                if (latest.current.source === 'ECB' && latest.current.currency.trim().toUpperCase() === code) {
                    onChange({ ...latest.current, rate: rateText(rate.unitsPerEuro, locale), rateDate: rate.rateDate });
                }
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind === 'aborted') return;
                setFetch({ status: apiError.status === 400 ? 'none' : 'unavailable' });
                // Geen ECB-koers: de gebruiker vult hem zelf in (bron Handmatig).
                onChange({ ...latest.current, rate: '', source: 'Manual', rateDate: null });
            });
        return () => controller.abort();
        // eslint-disable-next-line react-hooks/exhaustive-deps -- alleen bij een andere valuta, datum of bron ophalen
    }, [code, date, value.source, foreign]);

    const chooseCurrency = (next: string) => {
        onChange(isEuro(next)
            ? { currency: EURO, rate: '', source: 'NotApplicable', rateDate: null, touched: true }
            : { currency: next, rate: '', source: 'ECB', rateDate: null, touched: true });
    };
    const rate = effectiveRate(value);
    const known = currencies.some((currency) => currency.code === code) ? currencies : [...currencies, { code, hasEcbRate: false }];

    return (
        <div className="grid grid-cols-1 gap-x-3 gap-y-1 sm:grid-cols-[minmax(0,7rem)_minmax(0,1fr)]" data-testid="document-currency">
            <div className="max-w-[10rem] sm:max-w-none">
                <label htmlFor="currency-input" className="text-xs font-medium text-gray-700">{t('currency_field')}</label>
                <select {...fieldProps('currency', errors.currency)} value={code} disabled={disabled} className={inputClass} data-tour={`${prefix}-currency`}
                    onChange={(event) => chooseCurrency(event.target.value)}>
                    {known.map((currency) => <option key={currency.code} value={currency.code}>{currency.code}</option>)}
                </select>
                <FieldError id="currency" messages={errors.currency} />
            </div>
            {foreign && (
                <div>
                    <label htmlFor="exchangeRate-input" className="text-xs font-medium text-gray-700">{t('currency_rate_label', { currency: code })}</label>
                    <div className="flex items-center gap-2">
                        <span className="mt-1 shrink-0 text-sm text-gray-700">1 EUR =</span>
                        <div className="w-36 shrink-0">
                            <input {...fieldProps('exchangeRate', errors.exchangeRate)} inputMode="decimal" autoComplete="off" value={value.rate} disabled={disabled}
                                data-tour={`${prefix}-rate`} className={`${inputClass} text-right tabular-nums`}
                                onChange={(event) => onChange({ ...value, rate: event.target.value, source: 'Manual', rateDate: null })} />
                        </div>
                        <span className="mt-1 shrink-0 text-sm text-gray-700">{code}</span>
                    </div>
                    <p className="mt-1 text-xs text-gray-700" data-testid="currency-rate-source" aria-live="polite">
                        {fetch.status === 'loading' && t('exchange_rate_fetching')}
                        {fetch.status !== 'loading' && value.source === 'ECB' && value.rateDate && rate !== null
                            && t('currency_rate_ecb', { line: rateLine(code, rate, locale), date: formatDateOnly(value.rateDate, locale) })}
                        {fetch.status !== 'loading' && value.source === 'Manual' && <span className="font-medium">{t('currency_rate_manual')}</span>}
                        {rate !== null && <span className="block text-gray-600">{t('currency_rate_direction', { inverse: inverseLine(code, rate, locale) })}</span>}
                    </p>
                    {fetch.status === 'none' && <p className="text-xs font-medium text-amber-900" role="status">{t('currency_rate_none', { currency: code })}</p>}
                    {fetch.status === 'unavailable' && <p className="text-xs font-medium text-amber-900" role="status">{t('currency_rate_unavailable')}</p>}
                    {value.source === 'Manual' && !disabled && (
                        <button type="button" className="text-xs text-blue-800 underline underline-offset-2" data-testid="currency-use-ecb"
                            onClick={() => onChange({ ...value, rate: '', source: 'ECB', rateDate: null })}>{t('currency_use_ecb')}</button>
                    )}
                    <FieldError id="exchangeRate" messages={errors.exchangeRate} />
                </div>
            )}
        </div>
    );
}
