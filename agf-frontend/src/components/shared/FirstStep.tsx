'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { MASTER_DATA_HREF, type MasterDataNeed } from '../../lib/masterDataStatus';
import { primaryButtonClass } from '../supply/SuccessPanel';

// De ene duidelijke knop van een lege lijst ("Eerste relatie toevoegen", "Inkooporder aanmaken").
export function FirstStepLink({ href, label, dataTour }: { href: string; label: string; dataTour?: string }) {
    return <Link href={href} className={primaryButtonClass} data-tour={dataTour}>{label}</Link>;
}

// Een formulier waarvoor eerst iets anders nodig is (een inkooporder zonder leverancier in het systeem): zegt wat eerst moet en linkt
// ernaartoe. Blokkeert niets; het formulier blijft staan.
export function MissingMasterDataNotice({ missing }: { missing: readonly MasterDataNeed[] }) {
    const { t } = useTranslation();
    if (missing.length === 0) return null;
    return (
        <div role="note" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950" data-testid="missing-master-data">
            <p className="font-medium">{t('first_missing_title')}</p>
            <ul className="mt-1 space-y-1">
                {missing.map((need) => (
                    <li key={need} className="flex flex-wrap items-center gap-2" data-testid={`missing-${need}`}>
                        <span>{t(`first_missing_${need}`)}</span>
                        <Link href={MASTER_DATA_HREF[need]} className="font-medium text-blue-800 underline underline-offset-2">{t(`first_missing_${need}_link`)}</Link>
                    </li>
                ))}
            </ul>
        </div>
    );
}
