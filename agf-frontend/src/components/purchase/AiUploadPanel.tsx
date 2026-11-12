'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { toApiError } from '../../services/apiClient';
import { aiErrorCode, getAiReadingStatus, readInvoiceDocument } from '../../services/aiReadingService';
import type { AiExtraction, AiReadingStatus } from '../../types/aiReading';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// "Factuur uploaden" boven Inkoopfactuur invoeren: PDF, JPG, PNG of HEIC tot 10 MB. Staat AI-lezen niet aan (of is er geen sleutel, of is
// de maandlimiet bereikt), dan zegt het paneel dat, zonder nepgegevens; handmatig invoeren werkt altijd. Tijdens het lezen een melding met
// het model; een fout krijgt een uitleg in gewone taal en de knop Handmatig invoeren.
const ACCEPT = '.pdf,.jpg,.jpeg,.png,.heic,.heif,application/pdf,image/jpeg,image/png,image/heic,image/heif';
const ALLOWED = /\.(pdf|jpe?g|png|heic|heif)$/i;

export default function AiUploadPanel({ onProposal, beforeUpload, beforeApply }: {
    onProposal: (extraction: AiExtraction) => void;
    // Vraagt de gebruiker of het huidige concept vervangen mag worden; false = niet uploaden.
    beforeUpload?: () => boolean;
    // Na het lezen, vlak voor het toepassen: het formulier kan tijdens het lezen zijn aangepast. Vraag opnieuw (of weiger) als dat zo is;
    // false = het voorstel niet toepassen.
    beforeApply?: () => boolean;
}) {
    const { t } = useTranslation();
    const [status, setStatus] = useState<AiReadingStatus | null>(null);
    const [reading, setReading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [statusFailed, setStatusFailed] = useState(false);
    const [statusAttempt, setStatusAttempt] = useState(0);
    const input = useRef<HTMLInputElement | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        setStatusFailed(false);
        getAiReadingStatus(controller.signal).then(setStatus).catch(() => {
            if (controller.signal.aborted) return;
            setStatus(null);
            setStatusFailed(true);
        });
        return () => controller.abort();
    }, [statusAttempt]);

    const available = status !== null && status.enabled && status.configured && !status.limitReached;
    const reason = status === null ? null : !status.enabled ? t('ai_off') : !status.configured ? t('ai_not_configured', { reason: status.notConfiguredReason ?? '' })
        : status.limitReached ? t('ai_error_AI_MONTHLY_LIMIT') : null;

    const choose = async (file: File | undefined) => {
        if (input.current) input.current.value = '';
        if (!file || status === null) return;
        if (!ALLOWED.test(file.name)) { setError('file_unsupported'); return; }
        if (file.size === 0) { setError('file_empty'); return; }
        if (file.size > status.maxFileBytes) { setError('file_tooLarge'); return; }
        if (beforeUpload && !beforeUpload()) return;
        setError(null);
        setReading(true);
        try {
            const extraction = await readInvoiceDocument(file);
            if (!beforeApply || beforeApply()) onProposal(extraction);
        } catch (caught) {
            setError(aiErrorCode(toApiError(caught)));
        } finally {
            setReading(false);
        }
    };

    return (
        <section className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm" data-tour="pinvoice-ai-upload" aria-labelledby="ai-upload-title">
            <div className="flex flex-wrap items-center gap-3">
                <h2 id="ai-upload-title" className="sr-only">{t('ai_upload_button')}</h2>
                <button type="button" className={secondaryButtonClass} disabled={!available || reading} onClick={() => input.current?.click()} data-testid="ai-upload-button">
                    ⬆ {t('ai_upload_button')}
                </button>
                <input ref={input} type="file" accept={ACCEPT} className="sr-only" tabIndex={-1} aria-label={t('ai_upload_button')} data-testid="ai-upload-input" data-tour="pinvoice-ai-file"
                    onChange={(event) => void choose(event.target.files?.[0])} />
                {reading && status && <span role="status" className="font-medium text-blue-900" data-testid="ai-reading">{t('ai_reading', { model: status.primaryModel.name })}</span>}
                {!reading && available && status && (
                    <span className="text-gray-700">{status.fallbackEnabled && status.fallbackModel
                        ? t('ai_upload_hint', { model: status.primaryModel.name, fallback: status.fallbackModel.name })
                        : t('ai_upload_hint_single', { model: status.primaryModel.name })}</span>
                )}
                {statusFailed && (
                    <span role="alert" className="text-amber-950" data-testid="ai-status-failed">
                        {t('ai_status_failed')}{' '}
                        <button type="button" className="font-medium text-blue-800 underline underline-offset-2" onClick={() => setStatusAttempt((n) => n + 1)}>{t('ai_status_retry')}</button>
                    </span>
                )}
                {!reading && reason && (
                    <span className="text-gray-800" data-testid="ai-unavailable">
                        {reason}{' '}
                        {!status?.enabled && <Link href="/settings#ai-lezen" className="font-medium text-blue-800 underline underline-offset-2">{t('ai_off_admin')}</Link>}
                    </span>
                )}
            </div>
            {error && (
                <div role="alert" className="mt-2 flex flex-wrap items-center gap-2 rounded border border-amber-300 bg-amber-50 px-2 py-1.5 text-amber-950" data-testid="ai-error">
                    <span>{t(`ai_error_${error}`)}</span>
                    <button type="button" className="font-medium text-blue-800 underline underline-offset-2" onClick={() => setError(null)}>{t('ai_manual_enter')}</button>
                </div>
            )}
        </section>
    );
}
