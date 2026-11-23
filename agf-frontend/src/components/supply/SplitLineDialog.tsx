'use client';

import { KeyboardEvent, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import type { EditLine, SplitRemainder } from '../../lib/purchaseOrderEdit';
import type { PurchaseLineDraft } from '../../lib/purchaseOrderLines';
import { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

export interface SplitQuestion {
    key: string;
    index: number;
    stored: EditLine;
    draft: PurchaseLineDraft;
}

// Feedback 22 november: een deels ontvangen regel krijgt een ander artikel. Het ontvangen deel blijft op de oude regel (die is dan klaar),
// het open deel wordt een nieuwe regel met het nieuwe artikel. Per regel kiest de gebruiker of het restant van de oude regel nog verwacht
// wordt of vervalt; standaard vervalt het (dan telt niets dubbel). Modaal: Tab blijft in het venster, Escape is Terug, Enter op een keuze
// slaat op.
export default function SplitLineDialog({ questions, variantName, onCancel, onConfirm }: {
    questions: SplitQuestion[];
    variantName: (variantId: string) => string;
    onCancel: () => void;
    onConfirm: (choices: Record<string, SplitRemainder>) => void;
}) {
    const { t, locale } = useTranslation();
    const titleId = useId();
    const dialogRef = useRef<HTMLDivElement | null>(null);
    const [choices, setChoices] = useState<Record<string, SplitRemainder>>(() => Object.fromEntries(questions.map((q) => [q.key, 'Cancelled' as SplitRemainder])));

    useEffect(() => {
        const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        dialogRef.current?.querySelector<HTMLInputElement>('input:checked')?.focus();
        const onOutside = (event: FocusEvent) => {
            if (event.target instanceof Node && dialogRef.current?.contains(event.target) !== true) dialogRef.current?.querySelector<HTMLInputElement>('input:checked')?.focus();
        };
        document.addEventListener('focusin', onOutside);
        return () => {
            document.removeEventListener('focusin', onOutside);
            if (previous?.isConnected) setTimeout(() => previous.focus(), 0);
        };
    }, []);

    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') { event.preventDefault(); onCancel(); return; }
        if (event.key === 'Enter' && event.target instanceof HTMLInputElement) { event.preventDefault(); onConfirm(choices); return; }
        if (event.key !== 'Tab') return;
        const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('input:checked, button') ?? []);
        if (focusable.length === 0) return;
        event.preventDefault();
        const index = focusable.indexOf(document.activeElement as HTMLElement);
        focusable[(index + (event.shiftKey ? focusable.length - 1 : 1) + focusable.length) % focusable.length].focus();
    };

    return createPortal(
        <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4" onKeyDown={onKeyDown}>
            <div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby={titleId} data-tour="split-dialog" data-testid="split-dialog"
                className="max-h-full w-full overflow-y-auto rounded-t-lg bg-white p-4 pb-20 shadow-xl sm:max-w-xl sm:rounded-lg sm:pb-4">
                <h2 id={titleId} className="text-lg font-semibold text-gray-900">{t('po_split_title')}</h2>
                <ul className="mt-2 space-y-3 text-sm text-gray-900">
                    {questions.map((question) => {
                        const unit = question.stored.stored.unit ?? '';
                        const old = variantName(question.stored.opened.productVariantId);
                        const name = `split-${question.key}`;
                        return (
                            <li key={question.key} className="rounded border border-gray-200 px-3 py-2" data-testid={`split-question-${question.index}`}>
                                <p>{t('po_split_text', {
                                    n: String(question.index + 1), old, received: formatQuantity(question.stored.received, locale),
                                    ordered: formatQuantity(question.stored.stored.expectedQuantity, locale), unit, article: variantName(question.draft.productVariantId),
                                    quantity: question.draft.quantity.trim(),
                                })}</p>
                                <fieldset className="mt-2 space-y-1">
                                    <legend className="font-medium">{t('po_split_remainder', { old })}</legend>
                                    {(['Cancelled', 'Expected'] as const).map((choice) => (
                                        <label key={choice} className="flex items-start gap-2">
                                            <input type="radio" name={name} value={choice} checked={choices[question.key] === choice} data-tour="split-remainder" data-tour-key={`${question.index}-${choice}`}
                                                onChange={() => setChoices((current) => ({ ...current, [question.key]: choice }))} className="mt-1" />
                                            <span>{t(choice === 'Cancelled' ? 'po_split_cancelled' : 'po_split_expected')}</span>
                                        </label>
                                    ))}
                                </fieldset>
                            </li>
                        );
                    })}
                </ul>
                <div className="mt-4 flex flex-col gap-2 sm:flex-row sm:justify-end">
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('po_split_back')}</button>
                    <button type="button" className={primaryButtonClass} onClick={() => onConfirm(choices)} data-tour="split-confirm">{t('po_split_save')}</button>
                </div>
            </div>
        </div>,
        document.body,
    );
}
