'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type OrderLogisticsDraft, isInTransit } from '../../lib/orderLogistics';
import { getAddressProposals } from '../../services/purchaseOrderService';
import type { CustomerOption } from '../../types/referenceData';
import type { StockLocation } from '../../types/stockLocation';
import CustomerPicker from '../sales/CustomerPicker';
import FieldError, { fieldProps, inputClass } from './FieldError';
import ReceiveLocationField from './ReceiveLocationField';

// Feedback 22 november: waar de inkooporder ontvangen wordt (de eigen loods, of een cross-dock, externe opslag, de leverancier of Onderweg /
// direct naar klant), het laadadres (leeg als de leverancier levert; "Wij halen op" vult het adres van de leverancier) en het afleveradres
// (leeg = de eigen loods; bij Onderweg het adres van de klant, te kiezen). Alles blijft gewone tekst die de gebruiker kan aanpassen.
export default function OrderLogisticsFields({ value, onChange, locations, vendorId, customers, locked, errors, locationsFailed = false, onRetryLocations, ownWarehouseAddress = null, onAddLocation, defaultFailed = false }: {
    value: OrderLogisticsDraft;
    onChange: (next: OrderLogisticsDraft) => void;
    locations: readonly StockLocation[];
    vendorId: number | null;
    customers: readonly CustomerOption[];
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    // De lijst met locaties kon niet geladen worden: zeg dat, met opnieuw proberen (review), zodat Eigen loods niet stilzwijgend lijkt te kloppen.
    locationsFailed?: boolean;
    onRetryLocations?: () => void;
    // November 2026: het adres van de eigen loods (onder "Eigen loods") en Instellingen › Voorraadlocaties in een zijpaneel.
    ownWarehouseAddress?: string | null;
    onAddLocation?: () => void;
    defaultFailed?: boolean;
}) {
    const { t } = useTranslation();
    const [customerId, setCustomerId] = useState('');
    const [problem, setProblem] = useState<string | null>(null);
    const inTransit = isInTransit(locations, value.receiveLocationId);
    // Antwoorden komen asynchroon binnen: werk op de actuele waarde bij en negeer verouderde antwoorden.
    const latest = useRef(value);
    latest.current = value;
    const pickUpRequest = useRef(0);
    const customerRequest = useRef(0);
    const proposedDelivery = useRef<string | null>(null);

    const proposedLoading = useRef<string | null>(null);

    // Haalt het leveranciersadres op en vult het laadadres, tenzij de gebruiker dat zelf heeft ingevuld of aangepast.
    const proposeLoading = async (vendor: number | null) => {
        const request = ++pickUpRequest.current;
        setProblem(null);
        const stale = proposedLoading.current;
        if (vendor === null) {
            if (stale !== null && latest.current.loadingAddress === stale) onChange({ ...latest.current, loadingAddress: '' });
            proposedLoading.current = null;
            return;
        }
        try {
            const proposals = await getAddressProposals(vendor, null);
            if (request !== pickUpRequest.current) return;
            const current = latest.current;
            if (!current.pickUp) return;
            const untouched = current.loadingAddress.trim() === '' || (stale !== null && current.loadingAddress === stale);
            if (untouched) {
                proposedLoading.current = proposals.vendorAddress;
                onChange({ ...current, loadingAddress: proposals.vendorAddress ?? '' });
            }
            if (proposals.vendorAddress === null) setProblem(t('po_logistics_vendor_no_address'));
        } catch {
            if (request !== pickUpRequest.current) return;
            setProblem(t('po_logistics_address_failed'));
        }
    };

    const pickUp = async (checked: boolean) => {
        if (!checked) {
            pickUpRequest.current++;
            setProblem(null);
            proposedLoading.current = null;
            onChange({ ...latest.current, pickUp: false, loadingAddress: '' });
            return;
        }
        latest.current = { ...latest.current, pickUp: true };
        onChange(latest.current);
        await proposeLoading(vendorId);
    };

    // Een andere leverancier: vernieuw het voorstel en negeer lopende antwoorden voor de vorige leverancier.
    const previousVendor = useRef(vendorId);
    useEffect(() => {
        if (previousVendor.current === vendorId) return;
        previousVendor.current = vendorId;
        pickUpRequest.current++;
        if (latest.current.pickUp) void proposeLoading(vendorId);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [vendorId]);

    // Een lopend klantverzoek vervalt zodra het afleveradres handmatig wijzigt of de locatie geen Onderweg meer is.
    const cancelCustomerRequest = () => { customerRequest.current++; proposedDelivery.current = null; };
    useEffect(() => {
        if (!inTransit) cancelCustomerRequest();
    }, [inTransit]);

    const chooseCustomer = async (customer: CustomerOption | null) => {
        const request = ++customerRequest.current;
        setCustomerId(customer === null ? '' : String(customer.id));
        setProblem(null);
        // Het voorstel van de vorige klant vervalt, tenzij de gebruiker het adres inmiddels zelf heeft aangepast.
        if (proposedDelivery.current !== null && latest.current.deliveryAddress === proposedDelivery.current) {
            onChange({ ...latest.current, deliveryAddress: '' });
        }
        proposedDelivery.current = null;
        if (customer === null) return;
        try {
            const proposals = await getAddressProposals(null, customer.id);
            if (request !== customerRequest.current) return;
            // Geen adres bij deze klant: het oude afleveradres hoort bij een andere klant, dus het veld wordt leeg (review).
            if (proposals.customerAddress === null) {
                onChange({ ...latest.current, deliveryAddress: '' });
                setProblem(t('po_logistics_customer_no_address'));
            } else {
                proposedDelivery.current = proposals.customerAddress;
                onChange({ ...latest.current, deliveryAddress: proposals.customerAddress });
            }
        } catch {
            if (request !== customerRequest.current) return;
            onChange({ ...latest.current, deliveryAddress: '' });
            setProblem(t('po_logistics_address_failed'));
        }
    };

    // Een andere ontvangstlocatie. Van Direct naar klant naar een locatie: het afleveradres en de klant horen bij die route en vervallen (bij
    // opslaan leeg). Kan de lijst niet geladen worden, dan weten we de soort niet en wissen we niets.
    const chooseLocation = (id: string) => {
        const wasInTransit = isInTransit(locations, latest.current.receiveLocationId);
        const next = { ...latest.current, receiveLocationId: id };
        if (wasInTransit && !isInTransit(locations, id)) {
            cancelCustomerRequest();
            setCustomerId('');
            next.deliveryAddress = '';
        }
        latest.current = next;
        onChange(next);
    };
    // Een afleveradres zonder Direct naar klant (van vóór november 2026): nooit stil weg, wel zichtbaar met Leegmaken.
    const earlierDelivery = !inTransit && value.deliveryAddress.trim() !== '';

    return (
        <fieldset className="grid grid-cols-1 gap-3 rounded border border-gray-200 p-2 md:grid-cols-2" data-tour="supply-logistics">
            <legend className="px-1 text-xs font-semibold uppercase tracking-wide text-gray-600">{t('po_logistics_title')}</legend>
            {/* Waar het vandaan komt: standaard levert de leverancier; alleen bij Wij halen op een adres. */}
            <div>
                <span className="block text-xs font-medium text-gray-700">{t('po_logistics_loading')}</span>
                <label className="mt-1 flex min-h-8 items-center gap-2 text-sm text-gray-800">
                    <input type="checkbox" checked={value.pickUp} disabled={locked} data-tour="supply-pick-up" onChange={(event) => void pickUp(event.target.checked)} />
                    {t('po_logistics_pick_up')}
                </label>
                {value.pickUp ? (
                    <>
                        <label htmlFor="logistics.loadingAddress-input" className="sr-only">{t('po_logistics_loading')}</label>
                        <textarea {...fieldProps('logistics.loadingAddress', errors('logistics.loadingAddress'))} rows={3} maxLength={500} value={value.loadingAddress} disabled={locked}
                            placeholder={t('po_logistics_loading_placeholder')} data-tour="supply-loading-address" className={inputClass}
                            onChange={(event) => onChange({ ...value, loadingAddress: event.target.value })} />
                        <FieldError id="logistics.loadingAddress" messages={errors('logistics.loadingAddress')} />
                    </>
                ) : <span className="block text-xs text-gray-500" data-testid="logistics-vendor-delivers">{t('po_logistics_vendor_delivers')}</span>}
            </div>
            {/* Waar het binnenkomt, of bij Direct naar klant: naar welke klant. */}
            <div>
                <ReceiveLocationField value={value.receiveLocationId} onChange={chooseLocation} locations={locations}
                    locked={locked} errors={errors} ownWarehouseAddress={ownWarehouseAddress} locationsFailed={locationsFailed} onRetryLocations={onRetryLocations}
                    onAddLocation={onAddLocation} defaultFailed={defaultFailed} />
                {inTransit && (
                    <div className="mt-2" data-testid="logistics-delivery-customer">
                        <label htmlFor="logistics.deliveryAddress-input" className="text-xs font-medium text-gray-700">{t('po_logistics_delivery_customer')}</label>
                        {!locked && (
                            <div className="mt-1 text-xs text-gray-800">
                                <span className="block">{t('po_logistics_customer')}</span>
                                <CustomerPicker id="logistics.customer" customers={customers} value={customerId} onChange={(customer) => void chooseCustomer(customer)} dataTour="supply-delivery-customer" />
                            </div>
                        )}
                        <textarea {...fieldProps('logistics.deliveryAddress', errors('logistics.deliveryAddress'))} rows={3} maxLength={500} value={value.deliveryAddress} disabled={locked}
                            placeholder={t('po_logistics_delivery_customer_placeholder')} data-tour="supply-delivery-address" className={inputClass}
                            onChange={(event) => { cancelCustomerRequest(); onChange({ ...value, deliveryAddress: event.target.value }); }} />
                        <FieldError id="logistics.deliveryAddress" messages={errors('logistics.deliveryAddress')} />
                    </div>
                )}
                {earlierDelivery && (
                    <div className="mt-2 rounded border border-amber-200 bg-amber-50 px-2 py-1.5 text-xs text-gray-800" data-testid="logistics-delivery-earlier">
                        <span className="block font-medium">{t('po_logistics_delivery_earlier')}</span>
                        <span className="block whitespace-pre-line">{value.deliveryAddress}</span>
                        {!locked && (
                            <button type="button" className="mt-1 text-blue-800 underline underline-offset-2" data-testid="logistics-delivery-clear"
                                onClick={() => onChange({ ...latest.current, deliveryAddress: '' })}>{t('po_logistics_delivery_clear')}</button>
                        )}
                    </div>
                )}
            </div>
            {problem && <p role="status" className="text-xs text-amber-900 md:col-span-2">{problem}</p>}
        </fieldset>
    );
}
