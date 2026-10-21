'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { HISTORY_STEP, eventLabelKey, newestFirst, qualityDecisions } from '../../lib/inspectionList';
import { qualityFromEvent } from '../../lib/qualityTransitions';
import { PalletAuditEvent } from '../../types/inspection';
import QualityBadge from '../availability/QualityBadge';

// Alleen-lezen historie van één inhoudsregel. Standaard alleen keuringsbeslissingen (oude → nieuwe kwaliteit, persoon,
// datum/tijd, reden), nieuwste eerst, in stappen van vijf. "Alle activiteiten" toont ook reserveringen, vrijgaven en
// verzendingen met vertaalde eventnamen. Er wordt nooit iets weggelaten, alleen ingeklapt.
export default function LineHistory({ events }: { events: PalletAuditEvent[] }) {
    const { t, locale } = useTranslation();
    const [showAll, setShowAll] = useState(false);
    const [limit, setLimit] = useState(HISTORY_STEP);

    const decisions = qualityDecisions(events);
    const all = newestFirst(events);
    const list = showAll ? all : decisions;
    const shown = list.slice(0, limit);
    const hidden = list.length - shown.length;
    const toggleClass = (active: boolean) => `rounded px-2 py-0.5 text-xs ${active ? 'bg-white font-medium text-blue-800 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`;

    return (
        <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
                <div className="inline-flex gap-1 rounded bg-gray-100 p-0.5" role="tablist" aria-label={t('inspection_history_title')}>
                    <button type="button" role="tab" aria-selected={!showAll} onClick={() => { setShowAll(false); setLimit(HISTORY_STEP); }} className={toggleClass(!showAll)}>
                        {t('inspection_history_only_quality')}
                    </button>
                    <button type="button" role="tab" aria-selected={showAll} onClick={() => { setShowAll(true); setLimit(HISTORY_STEP); }} className={toggleClass(showAll)}>
                        {t('inspection_history_all')}
                    </button>
                </div>
            </div>
            {list.length === 0 ? (
                <p className="text-xs text-gray-600">{showAll ? t('inspection_history_empty') : t('inspection_history_no_quality')}</p>
            ) : (
                <ol className="space-y-1 text-xs text-gray-800">
                    {shown.map((event) => {
                        const from = qualityFromEvent(event.oldValue);
                        const to = qualityFromEvent(event.newValue);
                        const quality = event.eventType === 'QualityStatusChanged';
                        const labelKey = eventLabelKey(event.eventType);
                        return (
                            <li key={event.id} className="flex flex-wrap items-center gap-2 rounded border border-gray-200 bg-gray-50 px-2 py-1">
                                <span className="font-mono text-gray-600">{formatDateTime(event.occurredAt, locale)}</span>
                                {!quality && <span className="font-medium">{labelKey ? t(labelKey) : event.eventType}</span>}
                                {quality && from && <QualityBadge status={from} />}
                                {quality && to && <><span aria-hidden="true">→</span><QualityBadge status={to} /></>}
                                {event.eventType === 'ContentLineAdded' && to && <QualityBadge status={to} />}
                                <span>{t('inspection_event_by', { actor: event.actorReference ?? t('inspection_actor_not_given') })}</span>
                                {event.reason && <span className="italic">“{event.reason}”</span>}
                            </li>
                        );
                    })}
                </ol>
            )}
            {hidden > 0 && (
                <button type="button" onClick={() => setLimit((current) => current + HISTORY_STEP)} className="text-xs font-medium text-blue-800 underline underline-offset-2">
                    {t('inspection_history_more', { count: String(hidden) })}
                </button>
            )}
        </div>
    );
}
