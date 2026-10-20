'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { ReservationStatus } from '../../types/salesOrder';

const STYLES: Record<ReservationStatus, { key: string; icon: string; className: string }> = {
    Active: { key: 'sales_reservation_active', icon: '●', className: 'bg-blue-100 text-blue-800 border-blue-300' },
    Released: { key: 'sales_reservation_released', icon: '↩', className: 'bg-gray-100 text-gray-800 border-gray-300' },
    Consumed: { key: 'sales_reservation_consumed', icon: '✓', className: 'bg-emerald-100 text-emerald-800 border-emerald-300' },
    Corrected: { key: 'sales_reservation_corrected', icon: '↺', className: 'bg-amber-100 text-amber-900 border-amber-300' },
};

// Status altijd als icoon + tekst, nooit alleen als kleur.
export function ReservationStatusBadge({ status }: { status: ReservationStatus }) {
    const { t } = useTranslation();
    const style = STYLES[status];
    return (
        <span className={`inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs font-medium ${style.className}`}>
            <span aria-hidden="true">{style.icon}</span>{t(style.key)}
        </span>
    );
}

// Reservering op inhoud die niet (meer) goedgekeurd is: duidelijk gemarkeerd, komt rechtstreeks uit de server.
export function OnHoldBadge() {
    const { t } = useTranslation();
    return (
        <span className="inline-flex items-center gap-1 rounded border border-red-300 bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-800">
            <span aria-hidden="true">⚠</span>{t('sales_on_hold')}
        </span>
    );
}

export function OrderStatusBadge({ status }: { status: string }) {
    const { t } = useTranslation();
    const open = status === 'Open';
    return (
        <span className={`inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs font-medium ${open ? 'border-green-300 bg-green-100 text-green-800' : 'border-gray-400 bg-gray-200 text-gray-800'}`}>
            {open ? t('sales_status_open') : status === 'Cancelled' ? t('sales_status_cancelled') : status}
        </span>
    );
}

// Toewijzing van verwachte, klantgebonden DirectTrade-aanvoer: geen voorraad, geen kg, nooit groen. Icoon + tekst, nooit alleen kleur.
export function ExpectedAllocationBadge() {
    const { t } = useTranslation();
    return (
        <span className="inline-flex items-center gap-1 rounded border border-violet-300 bg-violet-100 px-2 py-0.5 text-xs font-semibold text-violet-900">
            <span aria-hidden="true">🔒</span>{t('sales_allocation_badge')}
        </span>
    );
}

// Verzonden DirectTrade-toewijzing. Bewust niet "afgeleverd" of "niet afgeleverd": het systeem weet alleen dat er nog geen
// afleverbevestiging is vastgelegd.
export function DirectTradeShippedBadge() {
    const { t } = useTranslation();
    return (
        <span className="inline-flex items-center gap-1 rounded border border-violet-400 bg-white px-2 py-0.5 text-xs font-semibold text-violet-950">
            <span aria-hidden="true">🚚</span>{t('sales_ship_badge')}
        </span>
    );
}

// Teruggenomen DirectTrade-verzending: de gebruiker heeft verklaard dat deze geregistreerde verzending niet heeft plaatsgevonden.
export function DirectTradeCorrectedBadge() {
    const { t } = useTranslation();
    return (
        <span className="inline-flex items-center gap-1 rounded border border-amber-400 bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-950">
            <span aria-hidden="true">↺</span>{t('sales_dt_corrected_badge')}
        </span>
    );
}

