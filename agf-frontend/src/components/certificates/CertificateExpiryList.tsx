'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly } from '../../lib/calendarDate';
import { attentionAction, certificateLink, shortTypeName } from '../../lib/certificateDisplay';
import { ApiError, toApiError } from '../../services/apiClient';
import { getCertificateOverview } from '../../services/certificateService';
import type { CertificateOverview } from '../../types/certificate';
import ErrorNotice from '../supply/ErrorNotice';
import CertificateStatusBadge from './CertificateStatusBadge';

// "Certificaten die bijna verlopen" (certificaten deel 1): verlopen en binnen de ingestelde dagen verlopend, gesorteerd op einddatum, met de
// relatie, de soort, de dagen die resten, wat te doen (leverancier: controleren; klant: opvragen) en een link. Op de startpagina de melding
// in de app (compact: alleen als er iets is, de eerste regels met een link naar alles); onder Stamgegevens › Certificaten volledig.
export default function CertificateExpiryList({ compact = false, limit = 6 }: { compact?: boolean; limit?: number }) {
    const { t, locale } = useTranslation();
    const [overview, setOverview] = useState<CertificateOverview | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [attempt, setAttempt] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setError(null);
        getCertificateOverview({ status: 'attention' }, controller.signal).then(setOverview)
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [attempt]);

    // Op de startpagina is een lege lijst geen melding, een laadfout wel (anders is die niet te onderscheiden van "niets verlopen").
    if (compact && !error && (overview === null || overview.items.length === 0)) return null;
    // Compact: de eerste regels met een link naar alles; volledig: alles, niets wordt afgekapt.
    const items = overview ? (compact ? overview.items.slice(0, limit) : overview.items) : [];
    return (
        <section aria-labelledby="certificate-expiry-title" data-testid="certificate-expiry" data-tour="certificate-expiry"
            className={`space-y-2 rounded-lg border p-4 ${compact ? 'border-amber-300 bg-amber-50' : 'border-gray-300 bg-white'}`}>
            <div className="flex flex-wrap items-baseline justify-between gap-2">
                <h2 id="certificate-expiry-title" className="text-base font-semibold text-gray-900">
                    {t('cert_expiry_title')}{overview ? ` (${overview.items.length})` : ''}
                </h2>
                {overview && <span className="text-xs text-gray-700">{t('cert_expiry_days', { days: String(overview.warningDays) })}</span>}
            </div>
            {error && (
                <ErrorNotice error={error} actions={
                    <button type="button" onClick={() => setAttempt((n) => n + 1)} data-testid="certificate-expiry-retry"
                        className="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm font-medium text-red-900 hover:bg-red-100">{t('availability_retry')}</button>
                } />
            )}
            {overview && overview.items.length === 0 && <p className="text-sm text-gray-600" data-testid="certificate-expiry-empty">{t('cert_expiry_none')}</p>}
            {items.length > 0 && (
                <ul className="divide-y divide-amber-200/70 text-sm">
                    {items.map((item) => {
                        const action = attentionAction(item);
                        return (
                            <li key={item.certificateId} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-1.5" data-testid={`certificate-expiry-${item.certificateId}`}>
                                <CertificateStatusBadge status={item.status} daysLeft={item.daysLeft} />
                                <Link href={certificateLink(item.relationId)} className="font-medium text-blue-800 underline-offset-2 hover:underline">
                                    {item.relationId === null ? t('cert_own_company') : item.relationName}
                                </Link>
                                <span className="text-gray-800">{shortTypeName(item.typeCode, item.typeName)} <span className="font-mono text-xs">{item.number}</span></span>
                                {item.validUntil && <span className="text-xs text-gray-700">{t('cert_expiry_until', { date: formatDateOnly(item.validUntil, locale) })}</span>}
                                <span className="basis-full text-xs text-gray-800 sm:basis-auto">{t(action.key, action.params)}</span>
                            </li>
                        );
                    })}
                </ul>
            )}
            {compact && overview && overview.items.length > items.length && (
                <Link href="/certificates" className="text-sm font-medium text-blue-800 underline underline-offset-2">{t('cert_expiry_all', { count: String(overview.items.length) })}</Link>
            )}
            {compact && overview && overview.items.length <= items.length && (
                <Link href="/certificates" className="text-sm font-medium text-blue-800 underline underline-offset-2">{t('cert_expiry_open')}</Link>
            )}
        </section>
    );
}
