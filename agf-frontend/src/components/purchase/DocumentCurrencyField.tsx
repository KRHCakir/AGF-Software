'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import type { CurrencyDraft } from '../../lib/documentCurrency';
import { EURO, isEuro } from '../../lib/exchangeRate';
import type { PurchaseCurrency } from '../../services/purchaseCurrencyService';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import ExchangeRateField from './ExchangeRateField';

// Valuta-ronde: valuta (uit de lijst onder Instellingen, euro standaard) en koers bovenaan een inkooporder. Een andere valuta krijgt de
// koersregel van ExchangeRateField (ECB-koers ingevuld, aanpasbaar als Handmatig). `fetchOnOpen` = false bij aanpassen: de vastgelegde
// koers blijft staan tot je een andere valuta kiest.
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
    const { t } = useTranslation();
    const code = value.currency.trim().toUpperCase();
    const chooseCurrency = (next: string) => {
        onChange(isEuro(next)
            ? { currency: EURO, rate: '', source: 'NotApplicable', rateDate: null, touched: true }
            : { currency: next, rate: '', source: 'ECB', rateDate: null, touched: true });
    };
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
            <ExchangeRateField value={value} onChange={onChange} documentDate={documentDate} errors={errors} disabled={disabled} fetchOnOpen={fetchOnOpen} prefix={prefix} />
        </div>
    );
}
