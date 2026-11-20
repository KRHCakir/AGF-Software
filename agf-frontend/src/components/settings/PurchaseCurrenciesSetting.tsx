'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getPurchaseCurrencies, type PurchaseCurrency, updatePurchaseCurrencies } from '../../services/purchaseCurrencyService';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

// Valuta-ronde: de valuta's waaruit je kiest bij een inkooporder en inkoopfactuur (Beheer). Euro staat er altijd in en vooraan. Per valuta
// staat of de ECB er een dagkoers voor publiceert (dan vult het scherm de koers zelf in) of dat de gebruiker de koers invult.
export default function PurchaseCurrenciesSetting() {
    const { t } = useTranslation();
    const [currencies, setCurrencies] = useState<PurchaseCurrency[] | null>(null);
    const [added, setAdded] = useState('');
    const [invalid, setInvalid] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getPurchaseCurrencies(controller.signal).then(setCurrencies)
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const save = async (codes: string[]) => {
        setSaving(true);
        setError(null);
        setSaved(false);
        try {
            setCurrencies(await updatePurchaseCurrencies(codes));
            setSaved(true);
            setAdded('');
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };
    const codes = (currencies ?? []).map((currency) => currency.code);
    const add = () => {
        const code = added.trim().toUpperCase();
        if (!/^[A-Z]{3}$/.test(code)) { setInvalid(true); return; }
        setInvalid(false);
        if (!codes.includes(code)) void save([...codes, code]);
    };

    return (
        <section id="valuta" className="space-y-2 rounded-md border border-gray-300 bg-white p-3" data-testid="purchase-currencies-setting" aria-labelledby="purchase-currencies-title">
            <h2 id="purchase-currencies-title" className="text-lg font-semibold text-gray-900">{t('settings_currencies_title')}</h2>
            <p className="text-sm text-gray-600">{t('settings_currencies_hint')}</p>
            {error && <ErrorNotice error={error} />}
            {currencies !== null && (
                <ul className="flex flex-wrap gap-2" aria-label={t('settings_currencies_title')}>
                    {currencies.map((currency) => (
                        <li key={currency.code} className="flex items-center gap-2 rounded border border-gray-300 bg-gray-50 px-2 py-1 text-sm" data-testid={`currency-${currency.code}`}>
                            <span className="font-mono font-semibold text-gray-900">{currency.code}</span>
                            {currency.code !== 'EUR' && <span className="text-xs text-gray-700">{t(currency.hasEcbRate ? 'settings_currencies_ecb' : 'settings_currencies_manual')}</span>}
                            {currency.code !== 'EUR' && (
                                <button type="button" className="text-xs text-blue-800 underline underline-offset-2" disabled={saving} aria-label={`${t('settings_currencies_remove')} ${currency.code}`}
                                    onClick={() => void save(codes.filter((code) => code !== currency.code))}>{t('settings_currencies_remove')}</button>
                            )}
                        </li>
                    ))}
                </ul>
            )}
            <form noValidate className="flex flex-wrap items-end gap-2" onSubmit={(event) => { event.preventDefault(); add(); }}>
                <div>
                    <label htmlFor="newCurrency-input" className="text-xs font-medium text-gray-700">{t('settings_currencies_add')}</label>
                    <input {...fieldProps('newCurrency', invalid ? [t('settings_currencies_invalid')] : undefined)} value={added} maxLength={3} placeholder={t('settings_currencies_add_placeholder')}
                        onChange={(event) => { setAdded(event.target.value); setInvalid(false); }} className={`${inputClass} w-24 font-mono uppercase`} />
                    <FieldError id="newCurrency" messages={invalid ? [t('settings_currencies_invalid')] : undefined} />
                </div>
                <button type="submit" className={primaryButtonClass} disabled={saving || currencies === null}>{t('settings_currencies_add')}</button>
            </form>
            {saved && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{t('settings_currencies_saved')}</p>}
        </section>
    );
}
