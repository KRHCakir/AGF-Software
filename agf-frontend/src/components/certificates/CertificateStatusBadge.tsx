'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import type { CertificateStatus } from '../../types/certificate';

// Status van een certificaat uit de einddatum, met kleur én tekst (nooit alleen kleur): Geldig (groen), Verloopt binnenkort (oranje, met
// de dagen die nog resten), Verlopen (rood) of Geen einddatum (grijs).
const STYLE: Record<CertificateStatus, string> = {
    Valid: 'bg-green-100 text-green-900 ring-green-300',
    ExpiringSoon: 'bg-amber-100 text-amber-950 ring-amber-400',
    Expired: 'bg-red-100 text-red-900 ring-red-300',
    NoEndDate: 'bg-gray-100 text-gray-800 ring-gray-300',
};

export function certificateStatusText(t: (key: string, params?: Record<string, string>) => string, status: CertificateStatus, daysLeft: number | null): string {
    if (status === 'ExpiringSoon') return daysLeft === 0 ? t('cert_status_ExpiringSoon_today') : t('cert_status_ExpiringSoon', { days: String(daysLeft ?? '') });
    if (status === 'Expired') return t('cert_status_Expired', { days: String(Math.abs(daysLeft ?? 0)) });
    return t(`cert_status_${status}`);
}

export default function CertificateStatusBadge({ status, daysLeft, testId, compact = false }: { status: CertificateStatus; daysLeft: number | null; testId?: string; compact?: boolean }) {
    const { t } = useTranslation();
    return (
        <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xs font-medium ring-1 ${STYLE[status]}`} data-status={status} data-testid={testId}>
            {compact && status === 'ExpiringSoon' ? t('cert_status_short_ExpiringSoon') : compact && status === 'Expired' ? t('cert_status_short_Expired') : certificateStatusText(t, status, daysLeft)}
        </span>
    );
}
