'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { PARTIJ_STATUS_LABEL_KEYS, type PartijStatus } from '../../lib/partijRules';

// Status van de partij in dezelfde stijl als de inkooporderlijst: Open (blauw), Uitverkocht (grijs, niets meer op voorraad of verwacht).
const STYLES: Record<PartijStatus, string> = {
    Open: 'border-blue-300 bg-blue-50 text-blue-900',
    SoldOut: 'border-gray-300 bg-gray-100 text-gray-700',
};

export default function PartijStatusBadge({ status }: { status: string }) {
    const { t } = useTranslation();
    const known = status === 'Open' || status === 'SoldOut' ? status : null;
    return (
        <span className={`inline-flex items-center whitespace-nowrap rounded border px-1.5 py-0.5 text-xs font-medium ${known ? STYLES[known] : STYLES.SoldOut}`} data-tour="partij-status" data-tour-key={status}>
            {known ? t(PARTIJ_STATUS_LABEL_KEYS[known]) : status}
        </span>
    );
}
