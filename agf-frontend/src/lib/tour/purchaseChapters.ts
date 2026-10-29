// Hoofdstuk 1 (Inkopen bestellen) en 2 (Ontvangen en keuren). Alleen data-tour-namen en vaste teksten; de voorcontrole
// levert de waarden die per database verschillen (snelcode, de open demo-inkooporder).

import { precheckPurchase, precheckReceive } from './prechecks';
import type { TourAction, TourChapter, TourStep } from './types';

const step = (prefix: string, id: string, route: string, actions: readonly TourAction[]): TourStep => ({
    id,
    route,
    titleKey: `tour_${prefix}_${id}_title`,
    whatKey: `tour_${prefix}_${id}_what`,
    whyKey: `tour_${prefix}_${id}_why`,
    actions,
});

const order = (id: string, route: string, actions: readonly TourAction[]) => step('order', id, route, actions);

export const ORDER_CHAPTER: TourChapter = {
    id: 'order',
    titleKey: 'tour_ch_order_title',
    summaryKey: 'tour_ch_order_summary',
    doneKey: 'tour_ch_order_done',
    writes: true,
    precheck: precheckPurchase,
    steps: [
        order('open', '/supply', [
            { kind: 'click', target: 'supply-destination:Warehouse' },
            { kind: 'click', target: 'supply-mode:plan' },
            { kind: 'highlight', target: 'supply-vendor' },
        ]),
        order('vendor', '/supply', [{ kind: 'pick', target: 'supply-vendor', text: '{{vendorSearch}}', expect: '{{vendorSearch}}' }]),
        order('reference', '/supply', [
            { kind: 'type', target: 'supply-reference', text: '{{tourRef}}' },
            { kind: 'highlight', target: 'supply-ordered-on' },
            { kind: 'type', target: 'supply-origin', text: 'Peru' },
            { kind: 'type', target: 'supply-expected', text: '{{expectedDate}}' },
        ]),
        order('article', '/supply', [
            { kind: 'pick', target: 'supply-line-product-0', text: '{{productCode}}' },
            { kind: 'select', target: 'supply-line-unit:0', value: '{{productUnit}}' },
            { kind: 'type', target: 'supply-line-quantity:0', text: '{{quantity}}' },
            { kind: 'highlight', target: 'supply-line-kg:0' },
        ]),
        order('save', '/supply', [
            { kind: 'click', target: 'supply-save' },
            { kind: 'wait', target: 'supply-order-number' },
            { kind: 'remember', target: 'supply-order-number', name: 'orderNumber', pattern: /(IO-\d{4}-\d+)/ },
            { kind: 'highlight', target: 'supply-order-number' },
        ]),
        order('find', '/purchase-orders', [
            { kind: 'type', target: 'po-search', text: '{{orderNumber}}' },
            { kind: 'click', target: 'po-open:{{orderNumber}}' },
            { kind: 'assert', target: 'po-detail-number', text: '{{orderNumber}}' },
        ]),
        order('invoice', '/purchase-orders', [
            { kind: 'click', target: 'po-add-invoice:Invoice' },
            { kind: 'type', target: 'po-invoice-number', text: 'F-{{tourRef}}' },
            { kind: 'click', target: 'po-invoice-save' },
            { kind: 'wait', target: 'po-invoice-ref:F-{{tourRef}}' },
            { kind: 'highlight', target: 'po-invoice-ref:F-{{tourRef}}' },
        ]),
        order('by-invoice', '/purchase-orders', [
            { kind: 'type', target: 'po-search', text: 'F-{{tourRef}}' },
            { kind: 'wait', target: 'po-open:{{orderNumber}}' },
            { kind: 'highlight', target: 'po-open:{{orderNumber}}' },
        ]),
    ],
};

const receive = (id: string, route: string, actions: readonly TourAction[]) => step('receive', id, route, actions);

export const RECEIVE_CHAPTER: TourChapter = {
    id: 'receive',
    titleKey: 'tour_ch_receive_title',
    summaryKey: 'tour_ch_receive_summary',
    doneKey: 'tour_ch_receive_done',
    writes: true,
    precheck: precheckReceive,
    steps: [
        receive('open', '/supply', [
            { kind: 'click', target: 'supply-destination:Warehouse' },
            { kind: 'click', target: 'supply-mode:receive' },
            { kind: 'type', target: 'receive-search', text: '{{orderId}}' },
        ]),
        receive('line', '/supply', [
            { kind: 'click', target: 'receive-line:{{orderId}}-{{lineId}}' },
            { kind: 'type', target: 'receive-lot:{{lineId}}', text: '{{tourRef}}' },
            { kind: 'type', target: 'receive-quantity:{{lineId}}', text: '{{quantity}}' },
            { kind: 'highlight', target: 'receive-weight:{{lineId}}' },
        ]),
        receive('register', '/supply', [
            { kind: 'click', target: 'receive-save' },
            { kind: 'wait', target: 'receive-to-inspection' },
            { kind: 'highlight', target: 'receive-to-inspection' },
        ]),
        receive('inspector', '/supply', [
            { kind: 'click', target: 'receive-to-inspection' },
            { kind: 'select', target: 'inspection-actor', value: 'other' },
            { kind: 'type', target: 'inspection-actor-other', text: '{{actor}}' },
        ]),
        receive('approve', '/inspection', [
            { kind: 'click', target: 'inspection-action:Approved-{{tourRef}}' },
            { kind: 'click', target: 'inspection-confirm:{{tourRef}}' },
            { kind: 'wait', target: 'inspection-status:{{tourRef}}-Approved' },
            { kind: 'highlight', target: 'inspection-status:{{tourRef}}-Approved' },
        ]),
        receive('reject', '/purchase-orders?id={{orderId}}', [
            { kind: 'click', target: 'po-reject:{{tourRef}}' },
            { kind: 'type', target: 'reject-quantity', text: '{{rejectQuantity}}' },
            { kind: 'select', target: 'reject-reason', value: 'Quality' },
            { kind: 'click', target: 'reject-decision:ReturnToSupplier' },
            { kind: 'click', target: 'reject-save' },
            { kind: 'wait', target: 'po-invoice-indicator:CreditNoteExpected' },
            { kind: 'highlight', target: 'po-invoice-indicator:CreditNoteExpected' },
        ]),
        receive('availability', '/availability?productVariantId={{variantId}}', [
            { kind: 'highlight', target: 'availability-available:{{variantId}}' },
        ]),
    ],
};
