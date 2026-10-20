'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { isActiveConfirmation } from '../../lib/deliveryState';
import { DeliveryConfirmation } from '../../types/salesOrder';

// Historie van ingetrokken afleverbevestigingen van één verzending, op volgnummer. De actieve bevestiging staat apart in de lijst.
export function DeliveryHistory({ history }: { history: DeliveryConfirmation[] }) {
    const { t, locale } = useTranslation();
    const retracted = history.filter((confirmation) => !isActiveConfirmation(confirmation));
    if (retracted.length === 0) return null;
    return (
        <ul className="mt-1 space-y-0.5 text-xs text-gray-700" aria-label={t('sales_delivery_history')} data-testid="delivery-history">
            {retracted.map((confirmation) => (
                <li key={confirmation.id}>
                    <span className="font-medium line-through decoration-gray-400">{t('sales_delivery_history_entry', { sequence: String(confirmation.sequence), date: formatDateOnly(confirmation.deliveredOn, locale) })}</span>
                    {' '}· {t('sales_delivery_history_retracted', { date: formatDateTime(confirmation.retraction!.recordedAt, locale), actor: confirmation.retraction!.actorReference })}
                    {' '}<span className="italic">“{confirmation.retraction!.reason}”</span>
                </li>
            ))}
        </ul>
    );
}
