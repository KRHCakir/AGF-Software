'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { unitLabelKey } from '../../lib/units';
import { toApiError } from '../../services/apiClient';
import { getMissingPartijen } from '../../services/partijService';
import { PACKAGE_UNITS, type PackageUnit } from '../../types/availability';
import type { MissingPartijLine } from '../../types/partij';

// Werklijst "Partij ontbreekt" (feedback 22 november): voorraad zonder partij. Sinds deze ronde kan die niet meer ontstaan (een order
// zonder partij krijgt er bij de eerste ontvangst een); in oude gegevens kan ze nog bestaan. Er wordt niets verzonnen: de gebruiker opent
// de inkooporder en koppelt de juiste partij (Partij › Inkooporder koppelen, met reden). Zonder zulke voorraad toont dit blok niets.
export default function MissingPartijWorklist({ reloadToken }: { reloadToken: number }) {
    const { t, locale } = useTranslation();
    const [lines, setLines] = useState<MissingPartijLine[]>([]);

    useEffect(() => {
        const controller = new AbortController();
        getMissingPartijen(controller.signal)
            .then(setLines)
            // Een werklijst naast de lijst: bij een fout tonen we niets in plaats van de partijen te blokkeren.
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setLines([]); });
        return () => controller.abort();
    }, [reloadToken]);

    if (lines.length === 0) return null;
    const unit = (value: string) => ((PACKAGE_UNITS as readonly string[]).includes(value) ? t(unitLabelKey(value as PackageUnit)) : value);
    const link = 'text-blue-700 underline hover:text-blue-900';

    return (
        <section data-tour="partij-missing" aria-labelledby="partij-missing-title" className="rounded border border-amber-300 bg-amber-50 p-3">
            <h2 id="partij-missing-title" className="font-semibold text-amber-900">{t('partij_missing_title', { count: String(lines.length) })}</h2>
            <p className="text-sm text-amber-900">{t('partij_missing_text')}</p>
            <ul className="mt-2 divide-y divide-amber-200 text-sm">
                {lines.map((line) => (
                    <li key={line.palletContentLineId} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 py-1.5">
                        <Link href={`/inspection?palletId=${line.palletId}`} className={`${link} font-semibold`}>{line.palletReference}</Link>
                        <span className="text-gray-900">{line.product}</span>
                        <span className="tabular-nums">{formatQuantity(line.onHandQuantity, locale)} {unit(line.unit)}</span>
                        {line.lotNumber !== null && <span className="text-gray-700">{t('partij_missing_lot', { lot: line.lotNumber })}</span>}
                        <span className="text-gray-700">{t('partij_missing_received', { date: formatCalendarDate(line.receivedAt, locale) })}</span>
                        <Link href={`/purchase-orders?id=${line.supplyCommitmentId}`} className={link}>
                            {line.vendorName === null ? line.orderNumber : `${line.orderNumber} · ${line.vendorName}`}
                        </Link>
                    </li>
                ))}
            </ul>
        </section>
    );
}
