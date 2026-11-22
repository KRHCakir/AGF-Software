'use client';

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getVatCheck } from '../../services/articleService';
import type { VatCheckItem } from '../../types/article';
import ErrorNotice from '../supply/ErrorNotice';
import { type SortColumns, sortRows } from '../../lib/listSort';
import { SortSelect } from '../shared/SortHeader';
import { useListSort } from '../shared/useListSort';

// Sorteren (feedback 22 november): een keuzelijst "Sorteren op" (de werklijst heeft geen kolomkoppen).
const KIND_ORDER = ['Article', 'Cost', 'Packaging'];
const VAT_CHECK_COLUMNS: SortColumns<VatCheckItem> = {
    description: { kind: 'text', value: (item) => item.description },
    where: { kind: 'number', value: (item) => KIND_ORDER.indexOf(item.kind) },
};
const VAT_CHECK_SORT_KEYS = Object.keys(VAT_CHECK_COLUMNS);

// "Btw nog invullen" (feedback 22 november): artikelen en kostenartikelen zonder btw-tarief. De migratie gaf producten 9% en diensten
// 21%; emballage hield haar eigen tarief. Een artikel open je in de lijst hierboven, een Dienst onder Instellingen › Kostenartikelen,
// emballage onder Emballage. Verborgen als alles een tarief heeft.
export default function VatCheck({ reloadToken, onOpenArticle }: { reloadToken: number; onOpenArticle: (id: number) => void }) {
    const { t } = useTranslation();
    const [items, setItems] = useState<VatCheckItem[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const { sort, setSort } = useListSort('vat-check', VAT_CHECK_SORT_KEYS);
    const sorted = useMemo(() => sortRows(items ?? [], VAT_CHECK_COLUMNS, sort), [items, sort]);

    useEffect(() => {
        const controller = new AbortController();
        getVatCheck(controller.signal)
            .then((list) => { setItems(list); setError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [reloadToken]);

    if (error === null && (items === null || items.length === 0)) return null;
    return (
        <section className="rounded-md border border-amber-300 bg-amber-50 p-3" aria-labelledby="vat-check" data-tour="vat-check">
            <h2 id="vat-check" className="text-sm font-semibold text-gray-900">{t('vat_check_title')}</h2>
            <p className="text-xs text-gray-700">{t('vat_check_hint')}</p>
            {error && <div className="mt-2"><ErrorNotice error={error} /></div>}
            {sorted.length > 1 && (
                <SortSelect id="vat-check-sort" className="mt-2 max-w-md" sort={sort} onChange={setSort} options={[
                    { key: 'description', label: t('worklist_sort_article'), kind: 'text' },
                    { key: 'where', label: t('worklist_sort_where'), kind: 'natural' },
                ]} />
            )}
            <ul className="mt-2 divide-y divide-amber-200 text-sm">
                {sorted.map((item) => (
                    <li key={`${item.kind}-${item.id}`} className="flex flex-wrap items-baseline justify-between gap-2 py-1.5" data-testid={`vat-check-${item.kind}-${item.id}`}>
                        {item.kind === 'Article'
                            ? <button type="button" className="text-left font-semibold text-blue-800 underline underline-offset-2" onClick={() => onOpenArticle(item.id)}>{item.description}</button>
                            : <span className="font-semibold text-gray-900">{item.description}</span>}
                        <span className="text-xs text-gray-700">{t(`vat_check_where_${item.kind}`)}</span>
                    </li>
                ))}
            </ul>
        </section>
    );
}
