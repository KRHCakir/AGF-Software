'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { qualityStyle } from '../../lib/availability';

// Kwaliteit altijd als icoon + tekst, nooit alleen als kleur.
export default function QualityBadge({ status }: { status: string }) {
    const { t } = useTranslation();
    const style = qualityStyle(status);
    if (style === null) {
        return <span className="inline-flex rounded border border-gray-300 px-2 py-0.5 text-xs text-gray-800">{status}</span>;
    }
    return (
        <span className={`inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs font-medium ${style.className}`}>
            <span aria-hidden="true">{style.icon}</span>
            {t(style.key)}
        </span>
    );
}
