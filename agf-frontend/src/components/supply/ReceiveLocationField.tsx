'use client';

import { type ChangeEvent, type KeyboardEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { hasSeveralOwnWarehouses, isInTransit, locationChoices, receiveLocationName } from '../../lib/orderLogistics';
import type { StockLocation } from '../../types/stockLocation';
import FieldError, { fieldProps, inputClass } from './FieldError';

const ADD = '__add';

// Ontvangstlocatie van een inkooporder (november 2026). Met één eigen loods is Eigen loods de standaard: een rustige regel met het adres en
// Afwijkend? (de enige tabstop), zodat leverancier kiezen en door naar de regels snel blijft. Afwijkend? toont op dezelfde plek de lijst met
// de focus erin; Escape of Terug naar standaard gaat terug. Een order met een andere locatie toont de lijst meteen. Met meer dan één eigen
// loods staat de lijst altijd open (de standaard voorgekozen door het formulier). Vergrendeld: alleen tekst.
export default function ReceiveLocationField({ value, onChange, locations, locked, errors, ownWarehouseAddress = null, locationsFailed = false, onRetryLocations, onAddLocation, defaultFailed = false }: {
    value: string;
    onChange: (id: string) => void;
    locations: readonly StockLocation[];
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    ownWarehouseAddress?: string | null;
    locationsFailed?: boolean;
    onRetryLocations?: () => void;
    // Opent Instellingen › Voorraadlocaties in een zijpaneel (zonder de order kwijt te raken).
    onAddLocation?: () => void;
    // De standaard ontvangstlocatie kon niet geladen worden (alleen van belang bij meer dan één eigen loods, voor een nieuwe order).
    defaultFailed?: boolean;
}) {
    const { t } = useTranslation();
    const [open, setOpen] = useState(false);
    const select = useRef<HTMLSelectElement | null>(null);
    const other = useRef<HTMLButtonElement | null>(null);
    const focus = useRef<'select' | 'other' | null>(null);
    const several = hasSeveralOwnWarehouses(locations);
    const inTransit = isInTransit(locations, value);
    const showList = !locked && (several || open || value !== '');

    useEffect(() => {
        if (focus.current === 'select' && select.current) select.current.focus();
        if (focus.current === 'other' && other.current) other.current.focus();
        focus.current = null;
    });

    const openList = () => { focus.current = 'select'; setOpen(true); };
    const backToDefault = () => { focus.current = 'other'; setOpen(false); onChange(''); };
    const choose = (event: ChangeEvent<HTMLSelectElement>) => {
        if (event.target.value === ADD) { onAddLocation?.(); return; }
        onChange(event.target.value);
    };
    const keyDown = (event: KeyboardEvent<HTMLSelectElement>) => {
        if (event.key === 'Escape' && !several) { event.preventDefault(); backToDefault(); }
    };

    const failed = locationsFailed && (
        <span role="alert" className="block text-xs text-amber-900" data-testid="logistics-locations-failed">
            {t('po_logistics_locations_failed')}{' '}
            {onRetryLocations && <button type="button" className="underline underline-offset-2" onClick={onRetryLocations}>{t('currency_list_retry')}</button>}
        </span>
    );

    if (!showList) {
        const own = value === '';
        return (
            <div data-testid="receive-location-text">
                <p className="text-sm text-gray-900">
                    <span className="text-xs font-medium text-gray-700">{t('po_logistics_location')}:</span>{' '}
                    <span data-testid="receive-location-name">{receiveLocationName(locations, value, t('stock_location_own'))}</span>
                </p>
                {own && ownWarehouseAddress && <span className="block whitespace-pre-line text-xs text-gray-500" data-testid="receive-location-address">{ownWarehouseAddress}</span>}
                {!locked && (
                    <button ref={other} type="button" className="mt-1 min-h-8 text-xs text-blue-800 underline underline-offset-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        data-tour="supply-receive-location" onClick={openList}>✎ {t('po_logistics_location_other')}</button>
                )}
                {failed}
            </div>
        );
    }

    return (
        <div>
            <label htmlFor="logistics.receiveLocationId-input" className="text-xs font-medium text-gray-700">{t('po_logistics_location')}</label>
            <select ref={select} {...fieldProps('logistics.receiveLocationId', errors('logistics.receiveLocationId'))} value={value} data-tour="supply-receive-location"
                onChange={choose} onKeyDown={keyDown} className={inputClass}>
                <option value="">{t('stock_location_own')}</option>
                {locationChoices(locations, value).map((location) => {
                    // Geen dubbele tekst als de naam al de soort is (de vaste locatie "Onderweg / direct naar klant").
                    const kind = t(`stock_location_kind_${location.kind}`);
                    return <option key={location.id} value={String(location.id)}>{location.name === kind ? location.name : `${location.name} · ${kind}`}</option>;
                })}
                {onAddLocation && <option value={ADD}>{t('po_logistics_location_add')}</option>}
            </select>
            <FieldError id="logistics.receiveLocationId" messages={errors('logistics.receiveLocationId')} />
            <span className="block text-xs text-gray-500">{t(inTransit ? 'po_logistics_location_in_transit' : 'po_logistics_location_hint')}</span>
            {several && defaultFailed && (
                <span role="alert" className="block text-xs text-amber-900" data-testid="receive-default-failed">
                    {t('po_logistics_default_failed')}{' '}
                    {onRetryLocations && <button type="button" className="underline underline-offset-2" onClick={onRetryLocations}>{t('currency_list_retry')}</button>}
                </span>
            )}
            {!several && (
                <button type="button" className="mt-1 text-xs text-blue-800 underline underline-offset-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                    data-testid="receive-location-back" onClick={backToDefault}>{t('po_logistics_location_back')}</button>
            )}
            {failed}
        </div>
    );
}
