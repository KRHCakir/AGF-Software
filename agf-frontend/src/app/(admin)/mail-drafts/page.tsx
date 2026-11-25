'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { EmptyState, LoadingRows } from '../../../components/availability/StateViews';
import ErrorNotice from '../../../components/supply/ErrorNotice';
import { mailDocumentName } from '../../../components/sales/MailDraftsPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatDateTime } from '../../../lib/availability';
import { ApiError, toApiError } from '../../../services/apiClient';
import { getOpenMailDrafts } from '../../../services/documentMailService';
import type { MailDraft } from '../../../types/documentMail';
import { SortSelect } from '../../../components/shared/SortHeader';
import { useListSort } from '../../../components/shared/useListSort';
import { type SortColumns, sortRows } from '../../../lib/listSort';
import PageTitle from '../../../components/shared/PageTitle';

// Sorteren (feedback 22 november): de lijst staat als regels zonder kolomkoppen, dus een keuzelijst "Sorteren op". Standaard nieuwste eerst.
const DRAFT_COLUMNS: SortColumns<MailDraft> = {
    order: { kind: 'natural', value: (draft) => draft.orderNumber ?? draft.documentNumber },
    customer: { kind: 'text', value: (draft) => draft.customerName },
    document: { kind: 'natural', value: (draft) => draft.documentNumber },
    to: { kind: 'text', value: (draft) => draft.to },
    status: { kind: 'text', value: (draft) => draft.status },
    created: { kind: 'date', value: (draft) => draft.createdAt },
};
const DRAFT_SORT_KEYS = Object.keys(DRAFT_COLUMNS);

// Verkoop › Te versturen (documenten deel 3): de mailconcepten die nog open staan of mislukten, over alle orders, nieuwste eerst. Controleren
// en versturen gebeurt op de order zelf (de link opent haar). Er gaat nooit iets weg zonder een klik op Versturen.
export default function MailDraftsPage() {
    const { t, locale } = useTranslation();
    const [drafts, setDrafts] = useState<MailDraft[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        getOpenMailDrafts(controller.signal).then(setDrafts).catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);
    const { sort, setSort } = useListSort('mail-drafts', DRAFT_SORT_KEYS);
    const rows = useMemo(() => (drafts === null ? [] : sortRows(drafts, DRAFT_COLUMNS, sort)), [drafts, sort]);

    return (
        <main className="space-y-3 p-4" data-testid="mail-drafts-page" data-tour="mail-drafts-page">
            <PageTitle>{t('mail_drafts_title')}</PageTitle>
            <p className="text-sm text-gray-700">{t('mail_drafts_page_text')}</p>
            {error && <ErrorNotice error={error} />}
            {!error && drafts === null && <LoadingRows label={t('mail_drafts_loading')} />}
            {drafts !== null && drafts.length === 0 && <EmptyState title={t('mail_drafts_empty')} />}
            {drafts !== null && drafts.length > 1 && (
                <SortSelect id="mail-drafts-sort" className="max-w-md" sort={sort} onChange={setSort} options={[
                    { key: 'order', label: t('mail_drafts_sort_order'), kind: 'natural' },
                    { key: 'customer', label: t('availability_col_customer'), kind: 'text' },
                    { key: 'document', label: t('mail_drafts_sort_document'), kind: 'natural' },
                    { key: 'to', label: t('mail_drafts_sort_to'), kind: 'text' },
                    { key: 'status', label: t('mail_drafts_sort_status'), kind: 'text' },
                    { key: 'created', label: t('mail_drafts_sort_created'), kind: 'date' },
                ]} />
            )}
            {drafts !== null && drafts.length > 0 && (
                <ul className="divide-y divide-gray-200 rounded-md border border-gray-200 bg-white">
                    {rows.map((draft) => (
                        <li key={draft.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm" data-testid="mail-drafts-row">
                            {draft.invoiceId
                                ? <Link href={`/sales-invoices?id=${draft.invoiceId}`} className="font-semibold text-blue-800 underline">{draft.documentNumber}</Link>
                                : <Link href={`/sales-orders?orderId=${draft.salesOrderId}`} className="font-semibold text-blue-800 underline">{draft.orderNumber}</Link>}
                            <span>{draft.customerName ?? ''}</span>
                            <span>{mailDocumentName(t, draft)}</span>
                            <span className="text-gray-700">{draft.to || t('mail_no_address')}</span>
                            <span className="rounded bg-gray-100 px-2 py-0.5 text-xs">{t(`mail_status_${draft.status}`)}</span>
                            {draft.missingRecipient && <span className="text-xs text-amber-900">{t('mail_missing_recipient', { name: draft.missingRecipient })}</span>}
                            <span className="ml-auto text-xs text-gray-600">{formatDateTime(draft.createdAt, locale)}</span>
                        </li>
                    ))}
                </ul>
            )}
        </main>
    );
}
