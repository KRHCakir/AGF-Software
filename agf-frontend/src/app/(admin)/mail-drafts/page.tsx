'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { EmptyState, LoadingRows } from '../../../components/availability/StateViews';
import ErrorNotice from '../../../components/supply/ErrorNotice';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatDateTime } from '../../../lib/availability';
import { ApiError, toApiError } from '../../../services/apiClient';
import { getOpenMailDrafts } from '../../../services/documentMailService';
import type { MailDraft } from '../../../types/documentMail';

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

    return (
        <main className="space-y-3 p-4" data-testid="mail-drafts-page" data-tour="mail-drafts-page">
            <h1 className="text-xl font-semibold text-gray-900">{t('mail_drafts_title')}</h1>
            <p className="text-sm text-gray-700">{t('mail_drafts_page_text')}</p>
            {error && <ErrorNotice error={error} />}
            {!error && drafts === null && <LoadingRows label={t('mail_drafts_loading')} />}
            {drafts !== null && drafts.length === 0 && <EmptyState title={t('mail_drafts_empty')} />}
            {drafts !== null && drafts.length > 0 && (
                <ul className="divide-y divide-gray-200 rounded-md border border-gray-200 bg-white">
                    {drafts.map((draft) => (
                        <li key={draft.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm" data-testid="mail-drafts-row">
                            <Link href={`/sales-orders?orderId=${draft.salesOrderId}`} className="font-semibold text-blue-800 underline">{draft.orderNumber}</Link>
                            <span>{draft.customerName ?? ''}</span>
                            <span>{t(draft.documentKind === 'TransportOrder' ? 'so_transport_order_name' : 'so_slip_name', { number: draft.documentNumber, version: String(draft.documentVersion) })}</span>
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
