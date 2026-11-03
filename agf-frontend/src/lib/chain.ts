import type { PurchaseOrderDetail } from '../types/purchaseOrder';
import type { SalesOrder } from '../types/salesOrder';
import { dayMonth, orderLabel } from './salesSupply.ts';

// De keten-strip: Inkooporder > Ontvangst > Keuring > Voorraad > Verkooporder > Levering > Factuur, met per stap een status, aantallen
// en links naar het juiste scherm met het juiste filter. Alles afgeleid uit wat de API al geeft (lezen, nooit opslaan). Een stap die
// nog niet bestaat (factuur) is grijs "nog niet beschikbaar", zonder link. Geen React en geen vertalingen: met node:test te toetsen.

export type ChainStepKey = 'order' | 'receipt' | 'inspection' | 'stock' | 'sales' | 'delivery' | 'invoice';
// done = afgerond; partial = deels; open = nog te doen; empty = (nog) niets; unavailable = bestaat (nog) niet of hoort niet bij deze route.
export type ChainState = 'done' | 'partial' | 'open' | 'empty' | 'unavailable';

export interface ChainPart { key: string; params?: Record<string, string | number> }
export interface ChainLink { label: string; href: string; key?: string }

export interface ChainStep {
    key: ChainStepKey;
    state: ChainState;
    parts: ChainPart[];
    links: ChainLink[];
    current: boolean;
}

export const CHAIN_STEPS: readonly ChainStepKey[] = ['order', 'receipt', 'inspection', 'stock', 'sales', 'delivery', 'invoice'];
const MAX_LINKS = 3;

const sum = (values: number[]) => values.reduce((total, value) => total + value, 0);
const unique = <T,>(values: T[]) => [...new Set(values)];
const singleUnit = (units: (string | null | undefined)[]) => {
    const all = unique(units.filter((unit): unit is string => !!unit));
    return all.length === 1 ? all[0] : '';
};
const hasMixedUnits = (units: (string | null | undefined)[]) => unique(units.filter((unit): unit is string => !!unit)).length > 1;
// Bij gemengde eenheden (kg en colli) zou een som zonder eenheid misleiden: toon dan geen totaal maar "meerdere eenheden".
const qty = (mixed: boolean, part: ChainPart): ChainPart => (mixed ? { key: 'chain_mixed_units' } : part);

// Vergelijk per eenheid (kg en colli nooit bij elkaar optellen): voldaan als in elke eenheid de som van `done` de som van `target` haalt.
function metPerUnit<T extends { unit?: string | null }>(lines: T[], target: (line: T) => number, done: (line: T) => number): boolean {
    const units = unique(lines.map((line) => line.unit ?? ''));
    return units.every((u) => {
        const group = lines.filter((line) => (line.unit ?? '') === u);
        return sum(group.map(done)) >= sum(group.map(target));
    });
}

function availabilityHref(variantIds: number[]): string {
    const ids = unique(variantIds);
    const params = new URLSearchParams({ route: 'Warehouse' });
    if (ids.length === 1) params.set('productVariantId', String(ids[0]));
    params.set('focus', 'available');
    return `/availability?${params.toString()}`;
}

const invoiceStep = (): Omit<ChainStep, 'current'> => ({ key: 'invoice', state: 'unavailable', parts: [{ key: 'chain_not_yet' }], links: [] });

// Vanaf de inkooporder (detail) of vanaf een ontvangst van die order.
export function purchaseOrderChain(order: PurchaseOrderDetail, current: 'order' | 'receipt'): ChainStep[] {
    const direct = order.route === 'DirectTrade';
    const unit = singleUnit(order.lines.map((line) => line.unit));
    const mixed = hasMixedUnits(order.lines.map((line) => line.unit));
    const receipts = order.receipts;
    const pallets = unique(receipts.map((receipt) => receipt.palletId)).map((id) => ({ id, reference: receipts.find((r) => r.palletId === id)!.palletReference }));
    const sales = order.salesOrders ?? [];
    const steps: Omit<ChainStep, 'current'>[] = [];

    steps.push({ key: 'order', state: 'done', parts: [{ key: 'chain_order_vendor', params: { vendor: order.vendorName ?? '–' } }],
        links: [{ label: order.orderNumber, href: `/purchase-orders?id=${order.id}` }] });

    if (direct) {
        // Klantgebonden aanvoer gaat rechtstreeks naar de klant: geen ontvangst, keuring of voorraad bij ons.
        for (const key of ['receipt', 'inspection', 'stock'] as const) steps.push({ key, state: 'unavailable', parts: [{ key: 'chain_direct_trade' }], links: [] });
    } else {
        const received = order.receivedQuantity;
        const receiptState: ChainState = received <= 0 ? (order.status === 'Cancelled' ? 'empty' : 'open') : (mixed ? metPerUnit(order.lines, (line) => line.expectedQuantity, (line) => line.receivedQuantity) : received >= order.expectedQuantity) ? 'done' : 'partial';
        const receiptLinks: ChainLink[] = pallets.slice(0, MAX_LINKS).map((pallet) => ({ label: pallet.reference, href: `/inspection?palletId=${pallet.id}` }));
        if (receiptState !== 'done' && order.status !== 'Cancelled') receiptLinks.unshift({ label: '', key: 'chain_receive_action', href: `/receipts?order=${order.id}` });
        steps.push({
            key: 'receipt', state: receiptState, links: receiptLinks,
            parts: [
                qty(mixed, { key: 'chain_received_of', params: { received, expected: order.expectedQuantity, unit } }),
                ...(pallets.length > 0 ? [{ key: 'chain_pallets', params: { count: pallets.length } }] : []),
            ],
        });

        const inspectable = receipts.filter((receipt) => receipt.onHandQuantity > 0 || receipt.qualityStatus === 'Rejected');
        // Pallets tellen, geen regels: een pallet met meerdere productregels telt één keer per status.
        const palletCount = (list: typeof receipts) => unique(list.map((receipt) => receipt.palletId)).length;
        const open = palletCount(inspectable.filter((receipt) => receipt.qualityStatus === 'Quarantine' || receipt.qualityStatus === 'Blocked'));
        const approved = palletCount(receipts.filter((receipt) => receipt.qualityStatus === 'Approved'));
        const rejected = palletCount(receipts.filter((receipt) => receipt.qualityStatus === 'Rejected'));
        const firstOpen = receipts.find((receipt) => receipt.qualityStatus === 'Quarantine' || receipt.qualityStatus === 'Blocked');
        steps.push({
            key: 'inspection', state: receipts.length === 0 ? 'empty' : open > 0 ? 'partial' : 'done',
            parts: receipts.length === 0 ? [{ key: 'chain_nothing_yet' }] : [
                ...(open > 0 ? [{ key: 'chain_inspection_open', params: { count: open } }] : []),
                ...(approved > 0 ? [{ key: 'chain_inspection_approved', params: { count: approved } }] : []),
                ...(rejected > 0 ? [{ key: 'chain_inspection_rejected', params: { count: rejected } }] : []),
            ],
            links: receipts.length === 0 ? [] : [{ label: '', key: 'chain_to_inspection', href: firstOpen ? `/inspection?palletId=${firstOpen.palletId}` : `/inspection?palletId=${receipts[0].palletId}` }],
        });

        const inStock = sum(receipts.filter((receipt) => receipt.qualityStatus === 'Approved').map((receipt) => receipt.onHandQuantity - receipt.reservedQuantity));
        steps.push({
            key: 'stock', state: inStock > 0 ? 'done' : 'empty',
            parts: [inStock > 0 ? qty(mixed, { key: 'chain_stock_available', params: { quantity: inStock, unit } }) : { key: 'chain_stock_none' }],
            links: [{ label: '', key: 'chain_to_availability', href: availabilityHref(order.lines.map((line) => line.productVariantId)) }],
        });
    }

    const salesIds = unique(sales.map((link) => link.salesOrderId));
    const salesLinks = salesIds.slice(0, MAX_LINKS).map((id) => {
        const customer = sales.find((link) => link.salesOrderId === id)?.customerName;
        return { label: `#${id}${customer ? ` · ${customer}` : ''}`, href: `/sales-orders?orderId=${id}` };
    });
    const reserved = sum(sales.map((link) => link.reservedQuantity));
    const shipped = sum(sales.map((link) => link.shippedQuantity));
    const salesUnit = singleUnit(sales.map((link) => link.unit));
    const salesMixed = hasMixedUnits(sales.map((link) => link.unit));
    steps.push({
        key: 'sales', state: salesIds.length === 0 ? 'empty' : 'done', links: salesLinks,
        parts: salesIds.length === 0 ? [{ key: 'chain_nothing_yet' }] : [
            { key: 'chain_sales_orders', params: { count: salesIds.length } },
            ...(reserved > 0 ? [qty(salesMixed, { key: 'chain_reserved', params: { quantity: reserved, unit: salesUnit } })] : []),
        ],
    });
    steps.push({
        key: 'delivery', state: shipped > 0 ? (reserved > 0 ? 'partial' : 'done') : 'empty',
        parts: [shipped > 0 ? qty(salesMixed, { key: 'chain_shipped', params: { quantity: shipped, unit: salesUnit } }) :{ key: 'chain_nothing_yet' }],
        // Eerst de verkooporders die verzonden hebben, dan pas afkappen: een verzonden order na de eerste drie mist nooit.
        links: unique(sales.filter((link) => link.shippedQuantity > 0).map((link) => link.salesOrderId)).slice(0, MAX_LINKS)
            .map((id) => salesLinks.find((link) => link.href === `/sales-orders?orderId=${id}`) ?? { label: `#${id}`, href: `/sales-orders?orderId=${id}` }),
    });
    steps.push(invoiceStep());
    const at: ChainStepKey = current === 'order' ? 'order' : 'receipt';
    return steps.map((step) => ({ ...step, current: step.key === at }));
}

// Vanaf de verkooporder: de herkomst (inkooporder en partij) uit de reserveringen, dan de order zelf en de levering.
export function salesOrderChain(order: SalesOrder): ChainStep[] {
    const reservations = order.lines.flatMap((line) => line.reservations).filter((r) => r.status === 'Active' || r.status === 'Consumed');
    const physical = reservations.filter((r) => r.source !== 'DirectTrade');
    const unit = singleUnit(order.lines.map((line) => line.unit));
    const mixed = hasMixedUnits(order.lines.map((line) => line.unit));
    const purchaseOrders = unique(reservations.map((r) => r.supplyCommitmentId)).map((id) => ({ id, number: reservations.find((r) => r.supplyCommitmentId === id)?.supplyOrderNumber ?? `#${id}` }));
    const lots = unique(reservations.map((r) => r.lotNumber).filter((lot): lot is string => !!lot));
    const pallets = unique(physical.filter((r) => r.palletId !== null).map((r) => r.palletId!)).map((id) => ({ id, reference: physical.find((r) => r.palletId === id)?.palletReference ?? `#${id}` }));
    // Pallets tellen, geen reserveringen: meerdere verkoopregels op dezelfde pallet tellen één keer.
    const notApproved = unique(physical.filter((r) => r.status === 'Active' && r.contentQualityStatus !== null && r.contentQualityStatus !== 'Approved')
        .map((r) => r.palletId ?? r.palletContentLineId ?? r.id)).length;
    const demand = sum(order.lines.map((line) => line.quantity));
    const shipped = sum(order.lines.map((line) => line.dispatchedQuantity));
    const delivered = sum(order.lines.map((line) => line.deliveredQuantity));
    const stockReserved = sum(physical.filter((r) => r.status === 'Active').map((r) => r.quantity));
    const stockShipped = sum(physical.filter((r) => r.status === 'Consumed').map((r) => r.quantity));
    const nothing: ChainPart[] = [{ key: 'chain_nothing_reserved' }];
    // Verkocht vóór aankomst: het niet gereserveerde deel wacht op een verwachte inkooporder (vroegst verwacht eerst; oudste verkoop eerst
    // gedekt) of is tekort. Alleen bij een open order en alleen uit de dekking die de server meestuurt.
    const open = order.status === 'Open';
    const covers = open ? order.lines.flatMap((line) => line.coverage?.expected ?? []).filter((cover) => cover.quantity > 0) : [];
    const waiting = unique(covers.map((cover) => cover.supplyCommitmentId)).map((id) => {
        const of = covers.filter((cover) => cover.supplyCommitmentId === id);
        const dates = of.map((cover) => cover.expectedAt).filter((date): date is string => date !== null).sort();
        return { id, number: of[0].supplyOrderNumber ?? `#${id}`, expectedAt: dates[0] ?? null };
    });
    const shortage = open ? sum(order.lines.map((line) => line.coverage?.shortage ?? 0)) : 0;

    const steps: Omit<ChainStep, 'current'>[] = [
        {
            key: 'order', state: waiting.length > 0 ? (purchaseOrders.length > 0 ? 'partial' : 'open') : purchaseOrders.length > 0 ? 'done' : 'empty',
            parts: [
                ...(purchaseOrders.length > 0 ? (lots.length > 0 ? [{ key: 'chain_lots', params: { lots: lots.slice(0, MAX_LINKS).join(', ') } }] : []) : waiting.length > 0 ? [] : nothing),
                ...waiting.slice(0, MAX_LINKS).map((po): ChainPart => (po.expectedAt
                    ? { key: 'chain_waiting_for_po', params: { number: po.number, date: dayMonth(po.expectedAt) } }
                    : { key: 'chain_waiting_for_po_no_date', params: { number: po.number } })),
            ],
            links: [...purchaseOrders, ...waiting.filter((po) => !purchaseOrders.some((known) => known.id === po.id))]
                .slice(0, MAX_LINKS).map((po) => ({ label: po.number, href: `/purchase-orders?id=${po.id}` })),
        },
        {
            key: 'receipt', state: pallets.length > 0 ? 'done' : physical.length === 0 && reservations.length > 0 ? 'unavailable' : 'empty',
            parts: pallets.length > 0 ? [{ key: 'chain_pallets', params: { count: pallets.length } }] : reservations.length > 0 ? [{ key: 'chain_direct_trade' }] : nothing,
            links: pallets.slice(0, MAX_LINKS).map((pallet) => ({ label: pallet.reference, href: `/inspection?palletId=${pallet.id}` })),
        },
        {
            key: 'inspection', state: physical.length === 0 ? (reservations.length > 0 ? 'unavailable' : 'empty') : notApproved > 0 ? 'partial' : 'done',
            parts: physical.length === 0 ? (reservations.length > 0 ? [{ key: 'chain_direct_trade' }] : nothing)
                : [notApproved > 0 ? { key: 'chain_inspection_not_approved', params: { count: notApproved } } : { key: 'chain_inspection_all_approved' }],
            links: [],
        },
        {
            // Alleen magazijnvoorraad (Physical): gereserveerd, of (na verzending) al uit voorraad gehaald. Klantgebonden aanvoer komt
            // nooit in ons magazijn: dan is deze stap niet van toepassing, zonder link naar Beschikbaarheid.
            key: 'stock',
            state: stockReserved + stockShipped > 0 ? 'done' : physical.length === 0 && reservations.length > 0 ? 'unavailable' : 'empty',
            parts: [stockReserved > 0 ? qty(mixed, { key: 'chain_reserved', params: { quantity: stockReserved, unit } })
                : stockShipped > 0 ? qty(mixed, { key: 'chain_from_stock', params: { quantity: stockShipped, unit } })
                : physical.length === 0 && reservations.length > 0 ? { key: 'chain_direct_trade' } : { key: 'chain_nothing_reserved' }],
            links: physical.length === 0 && reservations.length > 0 ? [] : [{ label: '', key: 'chain_to_availability', href: availabilityHref(order.lines.map((line) => line.productVariantId)) }],
        },
        {
            key: 'sales', state: order.status === 'Cancelled' ? 'empty' : demand > 0 && (mixed ? metPerUnit(order.lines, (line) => line.quantity, (line) => line.dispatchedQuantity) : shipped >= demand) ? 'done' : 'partial',
            parts: [
                order.status === 'Cancelled' ? { key: 'chain_cancelled' } : qty(mixed, { key: 'chain_demand', params: { quantity: demand, unit } }),
                ...(shortage > 0 ? [qty(mixed, { key: 'chain_shortage', params: { quantity: shortage, unit } })] : []),
            ],
            links: [{ label: orderLabel(order), href: `/sales-orders?orderId=${order.id}` }],
        },
        {
            key: 'delivery', state: shipped <= 0 ? 'empty' : (mixed ? metPerUnit(order.lines, (line) => line.quantity, (line) => line.deliveredQuantity) : delivered >= demand) ? 'done' : 'partial',
            parts: shipped <= 0 ? [{ key: 'chain_nothing_yet' }] : [
                qty(mixed, { key: 'chain_shipped', params: { quantity: shipped, unit } }),
                ...(delivered > 0 && !mixed ? [{ key: 'chain_delivered', params: { quantity: delivered, unit } }] : []),
            ],
            links: [],
        },
        invoiceStep(),
    ];
    return steps.map((step) => ({ ...step, current: step.key === 'sales' }));
}
