'use client';

import { useTranslation } from '../../i18n/LanguageContext';

// GGN van een partij zoals bij ontvangst vastgelegd. Alleen weergave: het nummer staat er "zoals opgegeven"; de
// certificeringsstatus is niet gecontroleerd en wordt nergens geclaimd. Niets tonen als er geen GGN is vastgelegd.
export default function GgnBadge({ ggn }: { ggn: string | null | undefined }) {
    const { t } = useTranslation();
    if (!ggn) return null;
    return (
        <span className="inline-flex items-center gap-1 rounded border border-gray-300 bg-gray-50 px-1.5 text-xs text-gray-800" title={t('ggn_as_given')} data-testid="ggn-badge">
            <span className="font-medium">{t('ggn_label')}</span>
            <span className="font-mono">{ggn}</span>
        </span>
    );
}
