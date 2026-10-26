'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// "Controleer bij GLOBALG.A.P.": opent het portaaladres uit de instellingen in een nieuw tabblad (noopener) en zet de GGN op het
// klembord om daar te plakken. De controle zelf blijft handwerk; het resultaat legt de gebruiker vast (datum, wie, geldig tot).
// Lukt kopiëren niet (geen toestemming, geen veilige context), dan staat het nummer selecteerbaar in de melding.
export default function GgnPortalButton({ ggn, portalUrl, disabled }: { ggn: string; portalUrl: string; disabled?: boolean }) {
    const { t } = useTranslation();
    const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');

    const check = async () => {
        // Eerst openen (binnen de klik, anders blokkeert de browser het tabblad), daarna kopiëren.
        window.open(portalUrl, '_blank', 'noopener,noreferrer');
        try {
            if (typeof navigator === 'undefined' || !navigator.clipboard) throw new Error('no clipboard');
            await navigator.clipboard.writeText(ggn);
            setState('copied');
        } catch {
            setState('failed');
        }
    };

    return (
        <span className="inline-flex flex-wrap items-center gap-2">
            <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={disabled || ggn === ''} onClick={() => void check()} data-testid={`ggn-check-${ggn}`}>
                {t('relations_ggn_check')}
            </button>
            {state === 'copied' && <span role="status" className="text-xs text-green-800">{t('relations_ggn_copied', { ggn })}</span>}
            {state === 'failed' && <span role="status" className="text-xs text-amber-800">{t('relations_ggn_copy_failed')} <span className="select-all font-mono">{ggn}</span></span>}
        </span>
    );
}
