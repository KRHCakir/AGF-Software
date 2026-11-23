'use client';

import { FormEvent, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { changeReservationLot } from '../../services/salesOrderService';
import type { SalesOrderLine, StockReservation } from '../../types/salesOrder';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import LotPicker, { type ChosenLot } from './LotPicker';

// Feedback 22 november: bij het picken de partij van een reservering wijzigen. De server geeft de reservering vrij en reserveert dezelfde
// hoeveelheid uit de gekozen partij (oudste pallet eerst), in één opslag met auditregel. Eén sleutel per keuze: een herhaling na een
// netwerkfout doet niets dubbel; een andere partij is een nieuwe opdracht.
export default function ChangeLotForm({ line, reservation, onChanged, onCancel }: {
    line: SalesOrderLine;
    reservation: StockReservation;
    onChanged: () => Promise<unknown> | void;
    onCancel: () => void;
}) {
    const { t } = useTranslation();
    const [lot, setLot] = useState<ChosenLot | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const requestId = useRef<string>(newRequestId());

    const choose = (next: ChosenLot | null) => {
        if (next?.partijId !== lot?.partijId) requestId.current = newRequestId();
        setLot(next);
        setError(null);
    };
    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (busy || lot === null) return;
        setBusy(true);
        setError(null);
        try {
            await changeReservationLot(reservation.id, { requestId: requestId.current, expectedReservationVersion: reservation.version, partijId: lot.partijId });
            await onChanged();
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    return (
        <form onSubmit={(event) => void submit(event)} className="mt-2 space-y-2 rounded border border-blue-200 bg-blue-50 p-2" data-testid="change-lot-form">
            <p className="text-xs text-blue-950">{t('sales_change_lot_text', { lot: reservation.partijNumber ?? reservation.lotNumber ?? '–' })}</p>
            <LotPicker id={`change-lot-${reservation.id}`} productVariantId={line.productVariantId} unit={line.unit} value={lot} onChange={choose} disabled={busy}
                exclude={reservation.partijId ?? null} label={t('sales_change_lot_to')} dataTour="sales-change-lot-picker" />
            {error && <ErrorNotice error={error} />}
            <div className="flex flex-wrap gap-2">
                <button type="submit" className={primaryButtonClass} disabled={busy || lot === null} data-tour="sales-change-lot-save">{busy ? t('supply_saving') : t('sales_change_lot_save')}</button>
                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={onCancel}>{t('cancel')}</button>
            </div>
        </form>
    );
}
