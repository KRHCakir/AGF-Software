'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { qualityFromEvent } from '../../lib/qualityTransitions';
import { PalletAuditEvent } from '../../types/inspection';
import QualityBadge from '../availability/QualityBadge';

// Alleen-lezen historie van één inhoudsregel. Krijgt uitsluitend events met dezelfde palletContentLineId.
export default function LineHistory({ events }: { events: PalletAuditEvent[] }) {
    const { t, locale } = useTranslation();
    if (events.length === 0) {
        return <p className="text-xs text-gray-600">{t('inspection_history_empty')}</p>;
    }
    return (
        <ol className="space-y-1 text-xs text-gray-800">
            {events.map((event) => {
                const from = qualityFromEvent(event.oldValue);
                const to = qualityFromEvent(event.newValue);
                return (
                    <li key={event.id} className="flex flex-wrap items-center gap-2 rounded border border-gray-200 bg-gray-50 px-2 py-1">
                        <span className="font-mono text-gray-600">{formatDateTime(event.occurredAt, locale)}</span>
                        <span className="font-medium">
                            {event.eventType === 'ContentLineAdded' ? t('inspection_event_added')
                                : event.eventType === 'QualityStatusChanged' ? t('inspection_event_quality') : event.eventType}
                        </span>
                        {event.eventType === 'QualityStatusChanged' && from && <QualityBadge status={from} />}
                        {event.eventType === 'QualityStatusChanged' && to && <><span aria-hidden="true">→</span><QualityBadge status={to} /></>}
                        {event.eventType === 'ContentLineAdded' && to && <QualityBadge status={to} />}
                        <span>{t('inspection_event_by', { actor: event.actorReference ?? t('availability_unknown') })}</span>
                        {event.reason && <span className="italic">“{event.reason}”</span>}
                    </li>
                );
            })}
        </ol>
    );
}
