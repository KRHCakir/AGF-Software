'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { countryLabel } from '../../lib/countries';
import { ApiError, toApiError } from '../../services/apiClient';
import { getPurchaseOrderOriginCheck } from '../../services/purchaseOrderService';
import type { CountryReference } from '../../types/country';
import type { PurchaseOrderOriginCheck } from '../../types/purchaseOrder';
import ErrorNotice from '../supply/ErrorNotice';

// "Herkomst controleren" (feedback 22 november): inkooporders met een oude herkomsttekst die geen land uit de lijst is ("Peru (DEMO)").
// De migratie zette alleen eenduidige teksten om; de oude tekst is bewaard. Een open order krijg je via Aanpassen een land; bij een
// definitieve order verandert de kop niet meer en blijft de tekst staan (alleen ter info). Verborgen als er niets te controleren is.
export default function OrderOriginCheck({ countries, reloadToken }: { countries: CountryReference; reloadToken: number }) {
    const { t, locale } = useTranslation();
    const [items, setItems] = useState<PurchaseOrderOriginCheck[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        getPurchaseOrderOriginCheck(controller.signal)
            .then((list) => { setItems(list); setError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [reloadToken]);

    if (error === null && (items === null || items.length === 0)) return null;
    return (
        <section className="rounded-md border border-amber-300 bg-amber-50 p-3" aria-labelledby="order-origin-check" data-tour="order-origin-check">
            <h2 id="order-origin-check" className="text-sm font-semibold text-gray-900">{t('order_origin_check_title')}</h2>
            <p className="text-xs text-gray-700">{t('order_origin_check_hint')}</p>
            {error && <div className="mt-2"><ErrorNotice error={error} /></div>}
            <ul className="mt-2 divide-y divide-amber-200 text-sm">
                {(items ?? []).map((item) => (
                    <li key={item.id} className="flex flex-wrap items-baseline justify-between gap-2 py-1.5" data-testid={`order-origin-check-${item.id}`}>
                        <span className="min-w-0 break-words">
                            <Link href={`/purchase-orders?id=${item.id}`} className="font-semibold text-blue-800 underline underline-offset-2">{item.orderNumber}</Link>
                            {item.vendorName && <span className="text-gray-700"> · {item.vendorName}</span>}
                            <span className="block text-xs text-gray-700">{t('order_origin_check_text', { text: item.origin })}
                                {item.suggestions.length > 0 && <> · {t('order_origin_check_suggestion', { countries: item.suggestions.map((code) => countryLabel(countries, code, locale)).join(', ') })}</>}
                            </span>
                        </span>
                        <span className="text-xs text-gray-700">{item.definitive ? t('order_origin_check_definitive') : t('order_origin_check_open')}</span>
                    </li>
                ))}
            </ul>
        </section>
    );
}
