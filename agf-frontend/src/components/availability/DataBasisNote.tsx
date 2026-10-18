'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { AvailabilityBasis } from '../../types/availability';

// Transparant over wat de getallen wel en niet bevatten; de tekst volgt de vlaggen uit de API.
export default function DataBasisNote({ basis }: { basis: AvailabilityBasis }) {
    const { t } = useTranslation();
    const notes: string[] = [];
    notes.push(basis.reservationsApplied ? t('availability_basis_reservations_applied') : t('availability_basis_no_reservations'));
    if (!basis.legacyInventoryIncluded) {
        notes.push(t('availability_basis_no_legacy'));
    }
    notes.push(t('availability_basis_weights'));

    return (
        <aside aria-label={t('availability_basis_title')} className="flex gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-900">
            <span aria-hidden="true">ⓘ</span>
            <ul className="space-y-0.5">
                {notes.map((note) => <li key={note}>{note}</li>)}
            </ul>
        </aside>
    );
}
