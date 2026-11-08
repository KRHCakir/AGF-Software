'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { formatDateOnly, isDateOnly } from '../../lib/calendarDate';
import { formatRate, isEuro, toEuro } from '../../lib/exchangeRate';
import { toApiError } from '../../services/apiClient';
import { getExchangeRate } from '../../services/purchaseInvoiceService';
import type { ExchangeRate } from '../../types/partijCost';

type State = { status: 'idle' | 'loading' } | { status: 'ready'; rate: ExchangeRate } | { status: 'none' | 'unavailable' };

// Bij een factuur in een andere valuta: de ECB-koers voor de factuurdatum en het bedrag excl. btw in euro, als informatie tijdens het
// invoeren. Het systeem haalt de koers zelf op (de server bij de ECB); er is geen veld om hem te typen. Slaat niets op: bij het verdelen
// haalt de server de koers voor de opgeslagen factuur opnieuw op.
export default function ExchangeRatePreview({ currency, invoiceDate, net }: { currency: string; invoiceDate: string; net: number }) {
    const { t, locale } = useTranslation();
    const code = currency.trim().toUpperCase();
    const active = /^[A-Z]{3}$/.test(code) && !isEuro(code);
    const date = isDateOnly(invoiceDate) ? invoiceDate : null;
    const [state, setState] = useState<State>({ status: 'idle' });

    useEffect(() => {
        if (!active) {
            setState({ status: 'idle' });
            return;
        }
        const controller = new AbortController();
        const timer = window.setTimeout(() => {
            setState({ status: 'loading' });
            getExchangeRate(code, date, controller.signal)
                .then((rate) => setState({ status: 'ready', rate }))
                .catch((caught: unknown) => {
                    const apiError = toApiError(caught);
                    if (apiError.kind !== 'aborted') setState({ status: apiError.status === 400 ? 'none' : 'unavailable' });
                });
        }, 300);
        return () => { window.clearTimeout(timer); controller.abort(); };
    }, [active, code, date]);

    if (!active) return null;
    return (
        <div className="mt-1 rounded border border-sky-200 bg-sky-50 px-2 py-1 text-xs text-sky-950" data-testid="exchange-rate-preview" aria-live="polite">
            {state.status === 'loading' && t('exchange_rate_fetching')}
            {state.status === 'ready' && (
                <>
                    <span className="font-medium">{t('exchange_rate_preview', {
                        date: formatDateOnly(state.rate.rateDate, locale), rate: formatRate(state.rate.unitsPerEuro, locale), currency: code,
                        euro: formatMoney(toEuro(net, state.rate.unitsPerEuro), 'EUR', locale),
                    })}</span>
                    <span className="block text-sky-900">{t('exchange_rate_preview_hint')}</span>
                </>
            )}
            {state.status === 'none' && t('exchange_rate_none', { currency: code })}
            {state.status === 'unavailable' && t('exchange_rate_unavailable')}
        </div>
    );
}
