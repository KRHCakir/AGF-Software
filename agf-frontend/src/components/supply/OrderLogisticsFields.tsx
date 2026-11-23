'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type OrderLogisticsDraft, isInTransit, locationChoices } from '../../lib/orderLogistics';
import { getAddressProposals } from '../../services/purchaseOrderService';
import type { CustomerOption } from '../../types/referenceData';
import type { StockLocation } from '../../types/stockLocation';
import CustomerPicker from '../sales/CustomerPicker';
import FieldError, { fieldProps, inputClass } from './FieldError';

// Feedback 22 november: waar de inkooporder ontvangen wordt (de eigen loods, of een cross-dock, externe opslag, de leverancier of Onderweg /
// direct naar klant), het laadadres (leeg als de leverancier levert; "Wij halen op" vult het adres van de leverancier) en het afleveradres
// (leeg = de eigen loods; bij Onderweg het adres van de klant, te kiezen). Alles blijft gewone tekst die de gebruiker kan aanpassen.
export default function OrderLogisticsFields({ value, onChange, locations, vendorId, customers, locked, errors }: {
    value: OrderLogisticsDraft;
    onChange: (next: OrderLogisticsDraft) => void;
    locations: readonly StockLocation[];
    vendorId: number | null;
    customers: readonly CustomerOption[];
    locked: boolean;
    errors: (key: string) => string[] | undefined;
}) {
    const { t } = useTranslation();
    const [customerId, setCustomerId] = useState('');
    const [problem, setProblem] = useState<string | null>(null);
    const inTransit = isInTransit(locations, value.receiveLocationId);

    const pickUp = async (checked: boolean) => {
        setProblem(null);
        if (!checked) { onChange({ ...value, pickUp: false, loadingAddress: '' }); return; }
        if (vendorId === null) { onChange({ ...value, pickUp: true }); return; }
        try {
            const proposals = await getAddressProposals(vendorId, null);
            onChange({ ...value, pickUp: true, loadingAddress: value.loadingAddress.trim() !== '' ? value.loadingAddress : proposals.vendorAddress ?? '' });
            if (proposals.vendorAddress === null) setProblem(t('po_logistics_vendor_no_address'));
        } catch {
            onChange({ ...value, pickUp: true });
            setProblem(t('po_logistics_address_failed'));
        }
    };

    const chooseCustomer = async (customer: CustomerOption | null) => {
        setCustomerId(customer === null ? '' : String(customer.id));
        setProblem(null);
        if (customer === null) return;
        try {
            const proposals = await getAddressProposals(null, customer.id);
            if (proposals.customerAddress === null) setProblem(t('po_logistics_customer_no_address'));
            else onChange({ ...value, deliveryAddress: proposals.customerAddress });
        } catch {
            setProblem(t('po_logistics_address_failed'));
        }
    };

    return (
        <fieldset className="grid grid-cols-1 gap-3 rounded border border-gray-200 p-2 md:grid-cols-3" data-tour="supply-logistics">
            <legend className="px-1 text-xs font-semibold uppercase tracking-wide text-gray-600">{t('po_logistics_title')}</legend>
            <div>
                <label htmlFor="logistics.receiveLocationId-input" className="text-xs font-medium text-gray-700">{t('po_logistics_location')}</label>
                <select {...fieldProps('logistics.receiveLocationId', errors('logistics.receiveLocationId'))} value={value.receiveLocationId} disabled={locked} data-tour="supply-receive-location"
                    onChange={(event) => onChange({ ...value, receiveLocationId: event.target.value })} className={inputClass}>
                    <option value="">{t('stock_location_own')}</option>
                    {locationChoices(locations, value.receiveLocationId).map((location) => (
                        <option key={location.id} value={String(location.id)}>{location.name} · {t(`stock_location_kind_${location.kind}`)}</option>
                    ))}
                </select>
                <span className="block text-xs text-gray-500">{t(inTransit ? 'po_logistics_location_in_transit' : 'po_logistics_location_hint')}</span>
            </div>
            <div>
                <label htmlFor="logistics.loadingAddress-input" className="text-xs font-medium text-gray-700">{t('po_logistics_loading')}</label>
                <label className="mt-1 flex items-center gap-2 text-xs text-gray-800">
                    <input type="checkbox" checked={value.pickUp} disabled={locked} data-tour="supply-pick-up" onChange={(event) => void pickUp(event.target.checked)} />
                    {t('po_logistics_pick_up')}
                </label>
                <textarea {...fieldProps('logistics.loadingAddress', errors('logistics.loadingAddress'))} rows={3} maxLength={500} value={value.loadingAddress} disabled={locked}
                    placeholder={t('po_logistics_loading_placeholder')} data-tour="supply-loading-address" className={inputClass}
                    onChange={(event) => onChange({ ...value, loadingAddress: event.target.value })} />
                <FieldError id="logistics.loadingAddress" messages={errors('logistics.loadingAddress')} />
            </div>
            <div>
                <label htmlFor="logistics.deliveryAddress-input" className="text-xs font-medium text-gray-700">{t('po_logistics_delivery')}</label>
                {inTransit && !locked && (
                    <div className="mt-1 text-xs text-gray-800">
                        <span className="block">{t('po_logistics_customer')}</span>
                        <CustomerPicker id="logistics.customer" customers={customers} value={customerId} onChange={(customer) => void chooseCustomer(customer)} dataTour="supply-delivery-customer" />
                    </div>
                )}
                <textarea {...fieldProps('logistics.deliveryAddress', errors('logistics.deliveryAddress'))} rows={3} maxLength={500} value={value.deliveryAddress} disabled={locked}
                    placeholder={t('po_logistics_delivery_placeholder')} data-tour="supply-delivery-address" className={inputClass}
                    onChange={(event) => onChange({ ...value, deliveryAddress: event.target.value })} />
                <FieldError id="logistics.deliveryAddress" messages={errors('logistics.deliveryAddress')} />
            </div>
            {problem && <p role="status" className="text-xs text-amber-900 md:col-span-3">{problem}</p>}
        </fieldset>
    );
}
