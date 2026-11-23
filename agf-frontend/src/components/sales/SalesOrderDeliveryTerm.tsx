'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type DeliveryTermDraft, deliveryTermDraftOf, deliveryTermPayload, describeDeliveryTerm } from '../../lib/deliveryTerm';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { updateSalesOrderDeliveryTerm } from '../../services/salesOrderService';
import type { SalesOrder } from '../../types/salesOrder';
import DeliveryTermField from '../shared/DeliveryTermField';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// De leveringsconditie in de kop van een verkooporder (feedback 22 november): tonen en, zolang de order open is, per order aanpassen. De
// server legt het vast als auditregel met oud en nieuw; de klant verandert niet. Laadbon en transportopdracht tonen haar niet.
export default function SalesOrderDeliveryTerm({ order, onChanged }: { order: SalesOrder; onChanged: () => void }) {
    const { t } = useTranslation();
    const [editing, setEditing] = useState<DeliveryTermDraft | null>(null);
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const text = describeDeliveryTerm(order.deliveryTerm, t);

    const save = async () => {
        if (editing === null || saving) return;
        const term = deliveryTermPayload(editing);
        if (!term.ok) {
            setErrors(Object.fromEntries(term.errors.map((e) => [e.field, [t(`relations_error_${e.code}`)]])));
            return;
        }
        setSaving(true);
        setError(null);
        try {
            await updateSalesOrderDeliveryTerm(order.id, { requestId: newRequestId(), expectedOrderVersion: order.version, deliveryTerm: term.value });
            setEditing(null);
            onChanged();
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setErrors(apiError.fieldErrors ?? {});
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="col-span-full" data-testid="sales-delivery-term">
            <dt className="text-xs text-gray-600">{t('relations_terms_delivery')}</dt>
            {editing === null ? (
                <dd className="flex flex-wrap items-baseline gap-2 font-medium text-gray-900" data-tour="sales-detail-delivery-term">
                    <span>{text ?? t('delivery_term_none')}</span>
                    {order.status === 'Open' && (
                        <button type="button" className="text-xs font-medium text-blue-800 underline underline-offset-2" onClick={() => { setErrors({}); setError(null); setEditing(deliveryTermDraftOf(order.deliveryTerm)); }}>
                            {t('delivery_term_change')}
                        </button>
                    )}
                </dd>
            ) : (
                <dd className="mt-1 space-y-2 rounded border border-gray-300 bg-gray-50 p-2">
                    {error && <ErrorNotice error={error} />}
                    <DeliveryTermField value={editing} onChange={(next) => { setEditing(next); setErrors({}); }} errors={(key) => errors[key]} locked={saving} dataTour="sales-detail-delivery-term-edit"
                        hint={t('delivery_term_order_only')} />
                    <div className="flex gap-2">
                        <button type="button" className={primaryButtonClass} disabled={saving} onClick={() => void save()}>{saving ? t('supply_saving') : t('delivery_term_save')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={saving} onClick={() => setEditing(null)}>{t('delivery_term_cancel')}</button>
                    </div>
                </dd>
            )}
        </div>
    );
}
