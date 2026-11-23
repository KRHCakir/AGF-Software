'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { countryLabel } from '../../lib/countries';
import { ApiError, toApiError } from '../../services/apiClient';
import { getPurchaseOrderOriginCheck } from '../../services/purchaseOrderService';
import type { CountryReference } from '../../types/country';
import type { PurchaseOrderOriginCheck } from '../../types/purchaseOrder';
import ErrorNotice from '../supply/ErrorNotice';
import { type SortColumns, sortRows } from '../../lib/listSort';
import { SortSelect } from '../shared/SortHeader';
import { useListSort } from '../shared/useListSort';

// Sorteren (feedback 22 november): een keuzelijst "Sorteren op" (de werklijst heeft geen kolomkoppen).
const ORIGIN_CHECK_COLUMNS: SortColumns<PurchaseOrderOriginCheck> = {
    order: { kind: 'natural', value: (item) => item.orderNumber },
    vendor: { kind: 'text', value: (item) => item.vendorName },
    origin: { kind: 'text', value: (item) => item.origin },
    status: { kind: 'number', value: (item) => (item.definitive ? 1 : 0) },
};
const ORIGIN_CHECK_SORT_KEYS = Object.keys(ORIGIN_CHECK_COLUMNS);

// "Herkomst controleren" (feedback 22 november): inkooporders met een oude herkomsttekst die geen land uit de lijst is ("Peru (DEMO)").
// De migratie zette alleen eenduidige teksten om; de oude tekst is bewaard. Een open order krijg je via Aanpassen een land; bij een
// definitieve order verandert de kop niet meer en blijft de tekst staan (alleen ter info). Verborgen als er niets te controleren is.
export default function OrderOriginCheck({ countries, reloadToken }: { countries: CountryReference; reloadToken: number }) {
    const { t, locale } = useTranslation();
    const [items, setItems] = useState<PurchaseOrderOriginCheck[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const { sort, setSort } = useListSort('order-origin-check', ORIGIN_CHECK_SORT_KEYS);
    const sorted = useMemo(() => sortRows(items ?? [], ORIGIN_CHECK_COLUMNS, sort), [items, sort]);

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
            {sorted.length > 1 && (
                <SortSelect id="order-origin-check-sort" className="mt-2 max-w-md" sort={sort} onChange={setSort} options={[
                    { key: 'order', label: t('mail_drafts_sort_order'), kind: 'natural' },
                    { key: 'vendor', label: t('po_col_vendor'), kind: 'text' },
                    { key: 'origin', label: t('worklist_sort_origin_text'), kind: 'text' },
                    { key: 'status', label: t('po_col_status'), kind: 'natural' },
                ]} />
            )}
            <ul className="mt-2 divide-y divide-amber-200 text-sm">
                {sorted.map((item) => (
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
