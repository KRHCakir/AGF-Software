'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { normalizeInvoiceNumber } from '../../lib/purchaseOrderRules';
import { findOrdersByInvoiceNumber } from '../../services/purchaseOrderService';
import type { PurchaseOrderInvoiceMatch } from '../../types/purchaseOrder';
import OrderNumberText from './OrderNumberText';

// Bij het invoeren van een inkoopfactuur: hangt dit nummer al aan een inkooporder (van deze leverancier, als die gekozen
// is), dan "Hoort bij order 4521" (het ordernummer van de leverancier, met het eigen IO-nummer klein erachter) met een link; anders
// niets. Een mislukte opzoeking toont ook niets.
// Stap 4b: vóór deze stap bleef het bij die melding, opslaan legde niets vast. Met onUseOrder staat er per order "Regels overnemen":
// de regels van die order komen als gekoppelde factuurregels in het formulier en de koppeling wordt bij opslaan bewaard.
export default function InvoiceOrderNotice({ invoiceNumber, vendorId, onUseOrder }: { invoiceNumber: string; vendorId: number | null; onUseOrder?: (orderId: number) => void }) {
    const { t } = useTranslation();
    const [matches, setMatches] = useState<PurchaseOrderInvoiceMatch[]>([]);
    const number = normalizeInvoiceNumber(invoiceNumber);

    useEffect(() => {
        if (number === null) { setMatches([]); return; }
        const controller = new AbortController();
        const handle = setTimeout(() => {
            findOrdersByInvoiceNumber(number, vendorId, controller.signal).then(setMatches).catch(() => setMatches([]));
        }, 300);
        return () => { clearTimeout(handle); controller.abort(); };
    }, [number, vendorId]);

    const orders = matches.filter((match, index) => matches.findIndex((other) => other.id === match.id) === index);
    if (number === null || orders.length === 0) return null;
    return (
        <p className="mt-1 text-xs text-blue-900" data-testid="invoice-order-notice">
            {t('po_belongs_to_order')}{' '}
            {orders.map((match, index) => (
                <span key={match.id}>
                    {index > 0 && ', '}
                    <Link href={`/purchase-orders?id=${match.id}`} className="font-semibold underline">
                        <OrderNumberText id={match.id} orderNumber={match.orderNumber} supplierReference={match.supplierReference} />
                    </Link>
                    {onUseOrder && (
                        <button type="button" className="ml-1 font-medium underline" data-testid="invoice-order-notice-use" onClick={() => onUseOrder(match.id)}>{t('invoice_link_use_order')}</button>
                    )}
                </span>
            ))}
        </p>
    );
}
