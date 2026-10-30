'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { DEVIATION_KIND_LABEL_KEYS, DEVIATION_REASON_LABEL_KEYS, unitText, variantLabel } from '../../lib/receiptDeviation';
import type { PurchaseOrderDeviation } from '../../types/receiptDeviation';
import { sectionClass } from './sectionClass';

// Vastgelegde afwijkingen van een inkooporder: verwacht -> ontvangen (zoals op het moment van ontvangst), hoeveelheid, wie,
// wanneer en waarom. Alleen lezen; ongedaan maken bestaat (nog) niet.
export default function DeviationsSection({ deviations }: { deviations: readonly PurchaseOrderDeviation[] }) {
    const { t, locale } = useTranslation();
    return (
        <section className={sectionClass} aria-labelledby="po-deviations" data-tour="po-deviations">
            <h3 id="po-deviations" className="mb-2 text-sm font-semibold text-gray-900">{t('po_deviations')}</h3>
            <ul className="divide-y divide-gray-100 text-sm">
                {deviations.map((deviation) => (
                    <li key={deviation.id} className="py-1.5">
                        <span className="block break-words">
                            <span className="text-gray-700">{deviation.expected ? variantLabel(deviation.expected) : '–'}</span>
                            <span aria-hidden="true"> → </span><span className="sr-only">{t('deviation_received')}: </span>
                            <span className="font-medium text-gray-900">{deviation.received ? variantLabel(deviation.received) : '–'}</span>
                        </span>
                        <span className="block font-mono text-gray-800">
                            {formatQuantity(deviation.quantity, locale)} {unitText(t, deviation.unit)}
                            {deviation.palletReference && <> · {deviation.palletReference}</>}
                            {' · '}{deviation.kinds.map((kind) => t(DEVIATION_KIND_LABEL_KEYS[kind] ?? kind)).join(', ')}
                        </span>
                        <span className="block text-xs text-gray-600">
                            {deviation.actorReference ?? '–'} · {formatDateTime(deviation.createdAt, locale)}
                            {deviation.reason && <> · {t(DEVIATION_REASON_LABEL_KEYS[deviation.reason] ?? deviation.reason)}</>}
                            {deviation.reasonNote && <>: {deviation.reasonNote}</>}
                        </span>
                    </li>
                ))}
            </ul>
        </section>
    );
}
