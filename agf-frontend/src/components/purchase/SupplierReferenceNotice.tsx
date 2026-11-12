'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { supplierReferenceKey } from '../../lib/purchaseOrderRules';
import { findOrdersBySupplierReference } from '../../services/purchaseOrderService';
import type { PurchaseOrderSupplierReferenceMatch } from '../../types/purchaseOrder';
import OrderNumberText from './OrderNumberText';

// Bij Inkooporder aanmaken: heeft deze leverancier al een order met dit ordernummer (zonder hoofdletters, spaties en streepjes vergeleken),
// dan een waarschuwing met een link naar die order. Geen blokkade: twee orders op dezelfde bevestiging of partij kan gewoon. Een mislukte
// opzoeking toont niets. Bij een ander nummer of een andere leverancier verdwijnt de oude waarschuwing meteen; een afgebroken opzoeking
// (er is intussen verder getypt) telt niet. Gezocht wordt op de sleutel, dus "45-21" en "45 21" geven geen nieuwe opzoeking. Bij
// aanpassen telt de order zelf niet mee (excludeOrderId): haar eigen nummer is geen dubbel.
export default function SupplierReferenceNotice({ supplierReference, vendorId, excludeOrderId = null }: { supplierReference: string; vendorId: number | null; excludeOrderId?: number | null }) {
    const { t } = useTranslation();
    const [matches, setMatches] = useState<PurchaseOrderSupplierReferenceMatch[]>([]);
    const key = supplierReferenceKey(supplierReference);

    useEffect(() => {
        setMatches([]);
        if (key === null || vendorId === null) return;
        const controller = new AbortController();
        const handle = setTimeout(() => {
            findOrdersBySupplierReference(key, vendorId, controller.signal)
                .then((found) => { if (!controller.signal.aborted) setMatches(found.filter((match) => match.id !== excludeOrderId)); })
                .catch(() => { if (!controller.signal.aborted) setMatches([]); });
        }, 300);
        return () => { clearTimeout(handle); controller.abort(); };
    }, [key, vendorId, excludeOrderId]);

    if (key === null || vendorId === null || matches.length === 0) return null;
    return (
        <p role="status" className="mt-1 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-xs text-amber-900" data-testid="supplier-reference-duplicate" data-tour="supply-reference-duplicate">
            {t('po_supplier_reference_duplicate')}{' '}
            {matches.map((match, index) => (
                <span key={match.id}>
                    {index > 0 && ', '}
                    <Link href={`/purchase-orders?id=${match.id}`} target="_blank" className="font-semibold underline">
                        <OrderNumberText id={match.id} orderNumber={match.orderNumber} supplierReference={match.supplierReference} />
                    </Link>
                </span>
            ))}
        </p>
    );
}
