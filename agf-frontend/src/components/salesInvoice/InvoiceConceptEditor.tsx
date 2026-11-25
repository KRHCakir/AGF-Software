'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { buildLineUpdates, draftOf, VAT_CHOICES, type LineDraft } from '../../lib/salesInvoiceEdit';
import { toApiError } from '../../services/apiClient';
import { updateSalesInvoice } from '../../services/salesInvoiceService';
import type { SalesInvoice } from '../../types/salesInvoice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Verkoopfactuur 1: een concept bewerken. Per regel aantal (nooit meer dan geleverd: het maximum staat erbij), prijs, korting en btw
// (0, 9 of 21%), of de regel weghalen; de klantreferentie. Opslaan met de versie die de gebruiker zag; een wijziging door een ander
// geeft de vraag om te herladen. Tab loopt per regel door de velden, Enter in een veld slaat op.
export default function InvoiceConceptEditor({ invoice, onChanged, onReload }: { invoice: SalesInvoice; onChanged: (next: SalesInvoice) => void; onReload?: () => void }) {
    const { t, locale } = useTranslation();
    const [open, setOpen] = useState(false);
    const [drafts, setDrafts] = useState<LineDraft[]>(() => invoice.lines.map((line) => draftOf(line)));
    const [reference, setReference] = useState(invoice.referenceNumber ?? '');
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [message, setMessage] = useState<string | null>(null);
    const [stale, setStale] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        setDrafts(invoice.lines.map((line) => draftOf(line)));
        setReference(invoice.referenceNumber ?? '');
        setErrors({});
        setMessage(null);
        setStale(false);
    }, [invoice]);

    if (invoice.status !== 'Concept') return null;
    if (!open) {
        return <button type="button" className={secondaryButtonClass} data-tour="si-edit" onClick={() => setOpen(true)}>{t('si_edit')}</button>;
    }
    const change = (index: number, patch: Partial<LineDraft>) => setDrafts((current) => current.map((draft, i) => (i === index ? { ...draft, ...patch } : draft)));
    const save = async () => {
        const built = buildLineUpdates(drafts);
        if (!built.ok) { setErrors(built.errors); return; }
        setBusy(true);
        setErrors({});
        setMessage(null);
        try {
            onChanged(await updateSalesInvoice(invoice.id, { expectedVersion: invoice.version, referenceNumber: reference.trim() === '' ? null : reference.trim(), lines: built.value }));
            setOpen(false);
        } catch (caught) {
            const apiError = toApiError(caught);
            const fields = Object.fromEntries(Object.entries(apiError.fieldErrors).map(([key, value]) => [key, value.join(' ')]));
            setErrors(fields);
            if (apiError.status === 409) setStale(true);
            setMessage(Object.keys(fields).length > 0 ? null : apiError.message);
        } finally {
            setBusy(false);
        }
    };
    const error = (key: string) => errors[key] && <span className="block text-xs text-red-800" id={`${key}-error`}>{t(errors[key])}</span>;
    const described = (key: string) => (errors[key] ? `${key}-error` : undefined);

    return (
        <form className="space-y-2 rounded border border-gray-300 bg-gray-50 p-2" data-testid="si-editor" onSubmit={(event) => { event.preventDefault(); void save(); }}>
            <div className="overflow-x-auto">
                <table className="min-w-full text-sm">
                    <thead className="text-left text-xs font-semibold text-gray-700">
                        <tr>
                            <th className="px-1 py-1">{t('si_line_description')}</th>
                            <th className="px-1 py-1">{t('si_line_quantity')}</th>
                            <th className="px-1 py-1">{t('si_line_price')}</th>
                            <th className="px-1 py-1">{t('si_line_discount')}</th>
                            <th className="px-1 py-1">{t('si_line_vat')}</th>
                            <th className="px-1 py-1">{t('si_remove')}</th>
                        </tr>
                    </thead>
                    <tbody>
                        {drafts.map((draft, index) => {
                            const key = `lines[${index}]`;
                            const line = invoice.lines[index];
                            return (
                                <tr key={draft.id} className={draft.remove ? 'opacity-50' : ''} data-testid="si-edit-line">
                                    <td className="px-1 py-1 align-top">{line.description ?? '–'}{line.lotNumber && <span className="block font-mono text-xs text-gray-600">{line.lotNumber}</span>}</td>
                                    <td className="px-1 py-1 align-top">
                                        <input id={`${key}.quantity`} aria-label={t('si_line_quantity')} inputMode="decimal" className={`${inputClass} w-24`} disabled={draft.remove} value={draft.quantity}
                                            aria-invalid={!!errors[`${key}.quantity`]} aria-describedby={described(`${key}.quantity`)} data-tour="si-edit-quantity" data-tour-key={String(index)}
                                            onChange={(event) => change(index, { quantity: event.target.value })} />
                                        {draft.maxQuantity !== null && <span className="block text-xs text-gray-600">{t('si_edit_max', { max: formatQuantity(draft.maxQuantity, locale) })}</span>}
                                        {error(`${key}.quantity`)}
                                    </td>
                                    <td className="px-1 py-1 align-top">
                                        <input id={`${key}.unitPrice`} aria-label={t('si_line_price')} inputMode="decimal" className={`${inputClass} w-24`} disabled={draft.remove} value={draft.unitPrice}
                                            aria-invalid={!!errors[`${key}.unitPrice`]} aria-describedby={described(`${key}.unitPrice`)} data-tour="si-edit-price" data-tour-key={String(index)}
                                            onChange={(event) => change(index, { unitPrice: event.target.value })} />
                                        {error(`${key}.unitPrice`)}
                                    </td>
                                    <td className="px-1 py-1 align-top">
                                        <input id={`${key}.discountPercentage`} aria-label={t('si_line_discount')} inputMode="decimal" className={`${inputClass} w-16`} disabled={draft.remove} value={draft.discount}
                                            aria-invalid={!!errors[`${key}.discountPercentage`]} onChange={(event) => change(index, { discount: event.target.value })} />
                                        {error(`${key}.discountPercentage`)}
                                    </td>
                                    <td className="px-1 py-1 align-top">
                                        <select id={`${key}.vatPercentage`} aria-label={t('si_line_vat')} className={`${inputClass} w-20`} disabled={draft.remove} value={draft.vat}
                                            onChange={(event) => change(index, { vat: event.target.value })}>
                                            {VAT_CHOICES.map((rate) => <option key={rate} value={rate}>{rate}%</option>)}
                                        </select>
                                        {error(`${key}.vatPercentage`)}
                                    </td>
                                    <td className="px-1 py-1 align-top">
                                        <input type="checkbox" aria-label={t('si_remove_line', { description: line.description ?? '' })} className="h-4 w-4" checked={draft.remove}
                                            onChange={(event) => change(index, { remove: event.target.checked })} />
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
            <label htmlFor="si-edit-reference" className="block text-xs font-medium text-gray-700">{t('si_reference')}</label>
            <input id="si-edit-reference" className={`${inputClass} max-w-xs`} value={reference} maxLength={100} onChange={(event) => setReference(event.target.value)} />
            {message && <p role="alert" className="text-sm text-red-800">{message}</p>}
            {stale && onReload && <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>}
            <div className="flex flex-wrap gap-2">
                <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="si-edit-save">{t('si_save')}</button>
                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setOpen(false)}>{t('si_cancel')}</button>
            </div>
        </form>
    );
}
