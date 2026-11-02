'use client';

import { useEffect, useState } from 'react';
import { purchaseOrderChain } from '../../lib/chain';
import { getPurchaseOrder } from '../../services/purchaseOrderService';
import type { PurchaseOrderDetail } from '../../types/purchaseOrder';
import ChainStrip from '../shared/ChainStrip';

// De keten-strip van één of meer inkooporders die nog geladen moeten worden (bijvoorbeeld na het boeken van een ontvangst, of op
// Ontvangst met ?order=). Alleen lezen; lukt het laden niet, dan verschijnt er niets (de strip is een hulpmiddel, geen voorwaarde).
export default function PurchaseOrderChain({ orderIds, current, reloadKey }: { orderIds: readonly number[]; current: 'order' | 'receipt'; reloadKey?: string | number }) {
    const [orders, setOrders] = useState<PurchaseOrderDetail[]>([]);
    const ids = orderIds.join(',');

    useEffect(() => {
        const controller = new AbortController();
        const wanted = ids === '' ? [] : ids.split(',').map(Number);
        Promise.all(wanted.map((id) => getPurchaseOrder(id, controller.signal).catch(() => null)))
            .then((loaded) => setOrders(loaded.filter((order): order is PurchaseOrderDetail => order !== null)))
            .catch(() => setOrders([]));
        return () => controller.abort();
    }, [ids, reloadKey]);

    if (orders.length === 0) return null;
    return (
        <div className="space-y-2" data-testid="purchase-order-chain">
            {orders.map((order) => <ChainStrip key={order.id} steps={purchaseOrderChain(order, current)} />)}
        </div>
    );
}
