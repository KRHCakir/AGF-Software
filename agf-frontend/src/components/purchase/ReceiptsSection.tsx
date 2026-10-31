'use client';

import Link from 'next/link';
import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import type { PurchaseOrderDetail } from '../../types/purchaseOrder';
import QualityBadge from '../availability/QualityBadge';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import { sectionClass } from './sectionClass';
import { rejectReceiptKey } from '../../lib/pendingCommands';
import { usePendingCommands } from '../sales/PendingCommandsContext';
import RejectReceiptForm from './RejectReceiptForm';

// Ontvangsten van de order met hun keuring; per afkeurbare regel "Afkeuren" (hooguit één formulier tegelijk open). Een
// afkeuring met onbekende uitkomst staat altijd open (ook als de regel intussen niet meer afkeurbaar lijkt): alleen opnieuw.
export default function ReceiptsSection({ order, onChanged }: { order: PurchaseOrderDetail; onChanged: (order: PurchaseOrderDetail) => void }) {
    const { t, locale } = useTranslation();
    const [rejecting, setRejecting] = useState<number | null>(null);
    // Regels waarvan het formulier open blijft na een definitieve fout van een eerder bevroren afkeuring.
    const [kept, setKept] = useState<number[]>([]);
    const keep = (lineId: number, on: boolean) => setKept((current) => (on ? (current.includes(lineId) ? current : [...current, lineId]) : current.filter((id) => id !== lineId)));
    const { commands } = usePendingCommands();
    const frozen = (lineId: number) => rejectReceiptKey(order.id, lineId) in commands;
    const qty = (value: number) => formatQuantity(value, locale);

    return (
        <section className={sectionClass} aria-labelledby="po-receipts">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                <h3 id="po-receipts" className="text-sm font-semibold text-gray-900">{t('po_receipts')}</h3>
                <Link href="/inspection" className="text-sm text-blue-800 hover:underline">{t('po_to_inspection')}</Link>
            </div>
            {order.receipts.length === 0 && <p className="text-sm text-gray-600">{t('po_no_receipts')}</p>}
            <ul className="divide-y divide-gray-100 text-sm">
                {order.receipts.map((receipt) => (
                    <li key={receipt.palletContentLineId} className="py-2">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="break-words">
                                <span className="font-mono font-medium">{receipt.palletReference}</span> · {receipt.lotNumber}
                                {receipt.origin === 'RejectionSplit' && <span className="text-gray-600"> · {t('po_origin_split')}</span>}
                            </span>
                            <QualityBadge status={receipt.qualityStatus} />
                        </div>
                        <p className="font-mono text-gray-800">
                            {t('po_receipt_amounts', { received: qty(receipt.quantity), onHand: qty(receipt.onHandQuantity) })} · {formatKg(receipt.onHandNetWeightKg, locale)}
                            {receipt.reservedQuantity > 0 && <span className="text-gray-600"> · {t('po_receipt_reserved', { reserved: qty(receipt.reservedQuantity) })}</span>}
                        </p>
                        {receipt.qualityReason && <p className="text-xs text-gray-600">{receipt.qualityReason}</p>}
                        {receipt.expectedShownBeforeEntry && (
                            <p className="text-xs text-amber-900" data-testid={`po-expected-shown-${receipt.palletContentLineId}`}>
                                {t('po_receipt_expected_shown', { when: receipt.expectedShownAt ? formatDateTime(receipt.expectedShownAt, locale) : '–', who: receipt.expectedShownBy ?? '–' })}
                            </p>
                        )}
                        {receipt.rejectable && rejecting !== receipt.palletContentLineId && !kept.includes(receipt.palletContentLineId) && !frozen(receipt.palletContentLineId) && (
                            <button type="button" data-tour="po-reject" data-tour-key={receipt.lotNumber} className={`${secondaryButtonClass} mt-1 border-red-300 text-red-800 hover:bg-red-50`} onClick={() => setRejecting(receipt.palletContentLineId)}
                                aria-label={t('po_reject_line', { pallet: receipt.palletReference, lot: receipt.lotNumber })}>
                                {t('po_reject')}
                            </button>
                        )}
                        {(rejecting === receipt.palletContentLineId || kept.includes(receipt.palletContentLineId) || frozen(receipt.palletContentLineId)) && (
                            <RejectReceiptForm orderId={order.id} orderNumber={order.orderNumber} receipt={receipt}
                                onCancel={() => { setRejecting(null); keep(receipt.palletContentLineId, false); }}
                                onFailed={() => keep(receipt.palletContentLineId, true)}
                                onDone={(updated) => { setRejecting(null); keep(receipt.palletContentLineId, false); onChanged(updated); }} />
                        )}
                    </li>
                ))}
            </ul>
        </section>
    );
}
