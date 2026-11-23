'use client';

import type { ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type SortKind, type SortState, ariaSort, parseSort, sortOptionValue } from '../../lib/listSort';

// Een klikbare kolomkop (feedback 22 november): de kop is een knop, dus bereikbaar met Tab en te bedienen met Enter en spatie. Een
// klik sorteert oplopend, nog eens aflopend, een derde keer terug naar de standaard. Het pijltje toont de richting; aria-sort staat op de
// gesorteerde kolom. Extra inhoud onder de titel (bijvoorbeeld "in euro") staat in `children`.
export function SortHeader({ label, sortKey, sort, onSort, className = '', children }: {
    label: ReactNode;
    sortKey: string;
    sort: SortState | null;
    onSort: (key: string) => void;
    className?: string;
    children?: ReactNode;
}) {
    const direction = ariaSort(sort, sortKey);
    return (
        <th scope="col" className={className} aria-sort={direction}>
            <button type="button" onClick={() => onSort(sortKey)} data-sort-key={sortKey}
                className="inline-flex items-center gap-1 rounded text-left uppercase tracking-wide hover:text-gray-900 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-700">
                <span>{label}</span>
                <span aria-hidden="true" className={direction === undefined ? 'text-gray-400' : 'text-blue-800'} data-testid="sort-arrow">
                    {direction === 'ascending' ? '▲' : direction === 'descending' ? '▼' : '↕'}
                </span>
            </button>
            {children}
        </th>
    );
}

export interface SortOption { key: string; label: string; kind: SortKind }

const DIRECTION_KEYS: Record<SortKind, [string, string]> = {
    text: ['sort_text_asc', 'sort_text_desc'],
    natural: ['sort_number_asc', 'sort_number_desc'],
    number: ['sort_amount_asc', 'sort_amount_desc'],
    date: ['sort_date_asc', 'sort_date_desc'],
};

// "Sorteren op" voor lijsten die op een smal scherm als kaarten staan: dezelfde kolommen, per kolom beide richtingen, en Standaard.
export function SortSelect({ options, sort, onChange, className = '', id }: {
    options: readonly SortOption[];
    sort: SortState | null;
    onChange: (next: SortState | null) => void;
    className?: string;
    id: string;
}) {
    const { t } = useTranslation();
    const keys = options.map((option) => option.key);
    return (
        <div className={`flex items-center gap-2 text-sm ${className}`}>
            <label htmlFor={id} className="shrink-0 font-medium text-gray-700">{t('sort_by')}</label>
            <select id={id} value={sortOptionValue(sort)} onChange={(event) => onChange(parseSort(event.target.value === '' ? null : event.target.value, keys))}
                className="min-w-0 flex-1 rounded border border-gray-300 bg-white px-2 py-1.5 text-sm text-gray-900" data-tour="sort-select">
                <option value="">{t('sort_default')}</option>
                {options.flatMap((option) => [
                    <option key={`${option.key}:asc`} value={`${option.key}:asc`}>{option.label} · {t(DIRECTION_KEYS[option.kind][0])}</option>,
                    <option key={`${option.key}:desc`} value={`${option.key}:desc`}>{option.label} · {t(DIRECTION_KEYS[option.kind][1])}</option>,
                ])}
            </select>
        </div>
    );
}
