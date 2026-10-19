'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError } from '../../services/apiClient';

// Uitklapbare technische details bij een fout: HTTP-status en de letterlijke servermelding. De gelokaliseerde uitleg en de
// herstelactie staan erboven; er wordt nergens op de Engelse servertekst gematcht.
export default function TechnicalDetails({ error }: { error: ApiError }) {
    const { t } = useTranslation();
    if (error.kind !== 'http' || !error.message) return null;
    return (
        <details className="mt-1 text-xs">
            <summary className="cursor-pointer font-medium underline-offset-2 hover:underline">{t('error_technical_details')}</summary>
            <p className="mt-1 font-mono">
                {error.status !== null && <span>{t('error_http_status', { status: String(error.status) })} · </span>}
                <span>{t('error_server_message')} {error.message}</span>
            </p>
        </details>
    );
}
