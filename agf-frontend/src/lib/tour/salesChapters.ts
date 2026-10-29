// Hoofdstuk 3 (Verkopen en reserveren) en de lege plek voor hoofdstuk 4 (Factureren), dat volgt zodra facturen klaar zijn.

import { precheckSales } from './prechecks';
import type { TourAction, TourChapter, TourStep } from './types';

const sales = (id: string, actions: readonly TourAction[]): TourStep => ({
    id,
    route: '/sales-orders',
    titleKey: `tour_sales_${id}_title`,
    whatKey: `tour_sales_${id}_what`,
    whyKey: `tour_sales_${id}_why`,
    actions,
});

export const SALES_CHAPTER: TourChapter = {
    id: 'sales',
    titleKey: 'tour_ch_sales_title',
    summaryKey: 'tour_ch_sales_summary',
    doneKey: 'tour_ch_sales_done',
    writes: true,
    precheck: precheckSales,
    steps: [
        sales('new', [
            { kind: 'click', target: 'sales-new' },
            { kind: 'highlight', target: 'sales-customer' },
        ]),
        sales('customer', [
            { kind: 'pick', target: 'sales-customer', text: '{{customerSearch}}', expect: '{{customerName}}' },
            { kind: 'type', target: 'sales-reference', text: '{{tourRef}}' },
            { kind: 'type', target: 'sales-delivery', text: '{{deliveryDate}}' },
        ]),
        sales('line', [
            { kind: 'pick', target: 'sales-line-product-0', text: '{{productCode}}' },
            { kind: 'type', target: 'sales-line-quantity:0', text: '{{quantity}}' },
            { kind: 'highlight', target: 'sales-line-kg:0' },
        ]),
        sales('save', [
            { kind: 'click', target: 'sales-save' },
            { kind: 'click', target: 'sales-open-saved' },
            { kind: 'wait', target: 'sales-detail:{{tourRef}}' },
            { kind: 'highlight', target: 'sales-detail:{{tourRef}}' },
        ]),
        sales('stock', [
            { kind: 'click', target: 'sales-choose-stock' },
            { kind: 'wait', target: 'sales-stock:fit' },
            { kind: 'highlight', target: 'sales-stock:fit' },
            { kind: 'click', target: 'sales-stock:fit' },
        ]),
        sales('reserve', [
            { kind: 'type', target: 'sales-reserve-quantity', text: '{{quantity}}' },
            { kind: 'click', target: 'sales-reserve-confirm' },
            { kind: 'wait', target: 'sales-line-state:reserved' },
            { kind: 'highlight', target: 'sales-line-state:reserved' },
        ]),
        sales('dispatch', [
            { kind: 'click', target: 'sales-dispatch' },
            { kind: 'click', target: 'sales-dispatch-all' },
            { kind: 'click', target: 'sales-dispatch-save' },
            { kind: 'wait', target: 'sales-dispatch-done' },
            { kind: 'highlight', target: 'sales-dispatch-done' },
        ]),
        sales('summary', [{ kind: 'highlight', target: 'sales-detail:{{tourRef}}' }]),
    ],
};

export const INVOICE_CHAPTER: TourChapter = {
    id: 'invoice',
    titleKey: 'tour_ch_invoice_title',
    summaryKey: 'demo_tour_coming_soon',
    writes: true,
    comingSoon: true,
    steps: [],
};
