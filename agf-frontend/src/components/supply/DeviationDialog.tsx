'use client';

import { KeyboardEvent, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { AttributeKind, DEVIATION_REASONS, DEVIATION_REASON_LABEL_KEYS, DeviationReason, articleName, deviationRows, isWeightOnly, shortNames, unitText } from '../../lib/receiptDeviation';

const WITHOUT_KEYS: Record<AttributeKind, string> = {
    Origin: 'deviation_without_origin', Size: 'deviation_without_size', Packaging: 'deviation_without_packaging', TradeClass: 'deviation_without_trade_class', Brand: 'deviation_without_brand',
};
import type { DeviationPreview } from '../../types/receiptDeviation';
import { inputClass } from './FieldError';
import { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

export interface DeviationChoice {
    reason: DeviationReason | null;
    note: string | null;
}

// "Afwijking van de bestelling": besteld en ontvangen onder elkaar, alleen de afwijkende kenmerken in rood, en wat Ja doet.
// Nee is voorgeselecteerd (focus); Escape is Nee, dus Enter bevestigt nooit per ongeluk Ja. Een reden is optioneel; bij Anders
// is een toelichting verplicht (zoals de server eist).
export default function DeviationDialog({ preview, onNo, onYes }: {
    preview: DeviationPreview;
    onNo: () => void;
    onYes: (choice: DeviationChoice) => void;
}) {
    const { t, locale } = useTranslation();
    const titleId = useId();
    const noRef = useRef<HTMLButtonElement | null>(null);
    const [reason, setReason] = useState<DeviationReason | ''>('');
    const [note, setNote] = useState('');
    const [noteMissing, setNoteMissing] = useState(false);

    useEffect(() => { noRef.current?.focus(); }, []);

    const unit = unitText(t, preview.unit);
    const qty = (value: number) => formatQuantity(value, locale);
    const names = shortNames(preview, (kind) => t(WITHOUT_KEYS[kind]));
    // "Avocado Hass Peru" en "Zuid-Afrika"; zonder afwijkend kenmerk (alleen Other of gewicht) zijn de korte namen al de volledige omschrijving.
    const expectedText = !preview.kinds.some((kind) => kind !== 'Other' && kind !== 'Weight') ? names.expected : `${articleName(preview.expected)} ${names.expected}`;
    // Gewicht (netto) als extra rij: "4 kg per colli (40 kg)"; rood alleen als de server het als afwijking noemt.
    const kg = (value: number | null | undefined) => (value === null || value === undefined ? '–' : formatKg(value, locale));
    const weightText = (perPackage: number | null | undefined, total: number | null | undefined) =>
        t('deviation_weight_value', { perPackage: kg(perPackage), unit, total: kg(total) });
    const expectedWeight = weightText(preview.expectedKgPerPackage, preview.expectedNetKg);
    const receivedWeight = weightText(preview.receivedKgPerPackage, preview.receivedNetKg);
    const weightOnly = isWeightOnly(preview.kinds);
    const rows = [
        ...deviationRows(preview.expected, preview.received, preview.kinds),
        ...(preview.expectedKgPerPackage != null || preview.receivedKgPerPackage != null
            ? [{ labelKey: 'deviation_attr_weight', expected: expectedWeight, received: receivedWeight, differs: preview.kinds.includes('Weight') }]
            : []),
    ];

    const yes = () => {
        const trimmed = note.trim();
        if (reason === 'Other' && trimmed === '') {
            setNoteMissing(true);
            return;
        }
        onYes({ reason: reason === '' ? null : reason, note: reason === '' || trimmed === '' ? null : trimmed });
    };

    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            onNo();
        }
    };

    return createPortal(
        <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4" onKeyDown={onKeyDown}>
            <div role="alertdialog" aria-modal="true" aria-labelledby={titleId} data-tour="deviation-dialog"
                className="max-h-full w-full overflow-y-auto rounded-t-lg bg-white p-4 pb-20 shadow-xl sm:max-w-xl sm:rounded-lg sm:pb-4">
                <h2 id={titleId} className="text-lg font-semibold text-gray-900">{t('deviation_title')}</h2>
                <p className="mt-1 text-sm text-gray-900">
                    {weightOnly
                        ? t('deviation_question_weight', { order: preview.orderNumber, article: articleName(preview.expected), expected: expectedWeight, received: receivedWeight })
                        : t('deviation_question', { order: preview.orderNumber, expected: expectedText, received: names.received })}
                </p>

                <dl className="mt-3 divide-y divide-gray-100 rounded border border-gray-200 text-sm" data-testid="deviation-rows">
                    <div className="hidden bg-gray-50 px-2 py-1 text-xs font-semibold uppercase tracking-wide text-gray-600 sm:grid sm:grid-cols-3 sm:gap-2">
                        <span>{t('deviation_attribute')}</span><span>{t('deviation_expected')}</span><span>{t('deviation_received')}</span>
                    </div>
                    {rows.map((row) => (
                        <div key={row.labelKey} className={`px-2 py-1.5 sm:grid sm:grid-cols-3 sm:gap-2 ${row.differs ? 'bg-red-50' : ''}`} data-differs={row.differs || undefined}>
                            <dt className="text-xs font-medium text-gray-700 sm:text-sm">{t(row.labelKey)}</dt>
                            <dd className="break-words text-gray-900"><span className="text-xs text-gray-600 sm:hidden">{t('deviation_expected')}: </span>{row.expected}</dd>
                            <dd className={`break-words ${row.differs ? 'font-semibold text-red-800' : 'text-gray-900'}`}>
                                <span className="text-xs font-normal text-gray-600 sm:hidden">{t('deviation_received')}: </span>{row.received}
                            </dd>
                        </div>
                    ))}
                </dl>

                <p className="mt-3 text-sm text-gray-900" data-testid="deviation-consequence">
                    {weightOnly
                        ? t('deviation_consequence_weight', { perPackage: kg(preview.receivedKgPerPackage), unit, total: kg(preview.originalExpectedNetKgAfter) })
                        : preview.reducedQuantity > 0
                        ? t('deviation_consequence', { expected: names.expected, from: qty(preview.originalExpectedQuantity), to: qty(preview.originalExpectedQuantityAfter), received: names.received, quantity: qty(preview.quantity), unit })
                        : t('deviation_consequence_add_only', { expected: names.expected, received: names.received, quantity: qty(preview.quantity), unit })}
                    {preview.excessQuantity > 0 && <> {t('deviation_excess', { quantity: qty(preview.excessQuantity), unit })}</>}
                </p>

                <div className="mt-3 grid gap-2 sm:grid-cols-2">
                    <label className="text-xs font-medium text-gray-700">
                        {t('deviation_reason')}
                        <select value={reason} data-tour="deviation-reason" onChange={(event) => { setReason(event.target.value as DeviationReason | ''); setNoteMissing(false); }} className={inputClass}>
                            <option value="">{t('deviation_reason_none')}</option>
                            {DEVIATION_REASONS.map((value) => <option key={value} value={value}>{t(DEVIATION_REASON_LABEL_KEYS[value])}</option>)}
                        </select>
                    </label>
                    {reason !== '' && (
                        <label className="text-xs font-medium text-gray-700">
                            {t('deviation_reason_note')}{reason === 'Other' && ' *'}
                            <input value={note} maxLength={500} onChange={(event) => { setNote(event.target.value); setNoteMissing(false); }}
                                aria-invalid={noteMissing || undefined} className={inputClass} />
                            {noteMissing && <span role="alert" className="block text-xs text-red-800">{t('deviation_reason_note_required')}</span>}
                        </label>
                    )}
                </div>

                <div className="mt-4 flex flex-col gap-2 sm:flex-row sm:justify-end">
                    <button ref={noRef} type="button" data-tour="deviation-no" className={secondaryButtonClass} onClick={onNo}>{t('deviation_no')}</button>
                    <button type="button" data-tour="deviation-yes" className={primaryButtonClass} onClick={yes}>{t('deviation_yes')}</button>
                </div>
            </div>
        </div>,
        document.body,
    );
}
