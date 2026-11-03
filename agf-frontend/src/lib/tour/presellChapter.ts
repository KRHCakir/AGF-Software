// Hoofdstuk "Verkopen voor aankomst": in de AGF verkoop je vóór de goederen binnen zijn. Eerst verkopen met een tekort (meer dan vrij te
// verkopen, uit verwachte aanvoer), dan de open demo-inkooporder ontvangen en goedkeuren (dezelfde stappen als Ontvangen en keuren), dan
// de verkochte regel reserveren met "oudste partij eerst". Het tekort blijft staan tot er meer inkoop is: zo werkt het model.

import { precheckPresell } from './prechecks';
import { RECEIVE_CHAPTER } from './purchaseChapters';
import type { TourAction, TourChapter, TourStep } from './types';

const presell = (id: string, route: string, actions: readonly TourAction[]): TourStep => ({
    id,
    route,
    titleKey: `tour_presell_${id}_title`,
    whatKey: `tour_presell_${id}_what`,
    whyKey: `tour_presell_${id}_why`,
    actions,
});

const RECEIVE_STEPS = ['open', 'line', 'register', 'inspector', 'approve'];

export const PRESELL_CHAPTER: TourChapter = {
    id: 'presell',
    titleKey: 'tour_ch_presell_title',
    summaryKey: 'tour_ch_presell_summary',
    doneKey: 'tour_ch_presell_done',
    writes: true,
    precheck: precheckPresell,
    steps: [
        presell('free', '/availability?productVariantId={{variantId}}', [
            { kind: 'click', target: 'availability-view-sales' },
            { kind: 'wait', target: 'availability-free:{{variantId}}' },
            { kind: 'highlight', target: 'availability-free:{{variantId}}' },
        ]),
        presell('sell', '/sales-orders?new=1&variantId={{variantId}}&unit={{unit}}&quantity={{sellQuantity}}', [
            { kind: 'pick', target: 'sales-customer', text: '{{customerSearch}}', expect: '{{customerName}}' },
            { kind: 'type', target: 'sales-reference', text: '{{tourRef}}' },
            { kind: 'type', target: 'sales-delivery', text: '{{deliveryDate}}' },
            { kind: 'click', target: 'sales-line-source:Expected' },
            { kind: 'wait', target: 'sales-line-shortage:0' },
            { kind: 'highlight', target: 'sales-line-shortage:0' },
        ]),
        presell('save', '/sales-orders', [
            { kind: 'click', target: 'sales-save' },
            { kind: 'wait', target: 'sales-saved-number' },
            { kind: 'remember', target: 'sales-saved-number', name: 'salesNumber', pattern: /(VO-\d{4}-\d+)/ },
            { kind: 'highlight', target: 'sales-shortage-warning' },
        ]),
        // Ontvangen en goedkeuren van de open demo-inkooporder: exact de stappen (en teksten) van hoofdstuk 2.
        ...RECEIVE_CHAPTER.steps.filter((step) => RECEIVE_STEPS.includes(step.id)),
        presell('worklist', '/sales-orders', [
            { kind: 'wait', target: 'sales-awaiting-stock' },
            { kind: 'highlight', target: 'sales-awaiting-stock' },
        ]),
        presell('reserve', '/sales-orders', [
            { kind: 'type', target: 'sales-search', text: '{{salesNumber}}' },
            { kind: 'click', target: 'sales-open-order:{{salesNumber}}' },
            { kind: 'click', target: 'sales-reserve-fifo' },
            { kind: 'wait', target: 'sales-fifo-confirm' },
            { kind: 'highlight', target: 'sales-fifo-quantity:0' },
            { kind: 'click', target: 'sales-fifo-confirm' },
            // Na bevestigen sluit het voorstel en staat de knop weer klaar (er blijft vraag over: het tekort).
            { kind: 'wait', target: 'sales-reserve-fifo' },
            { kind: 'highlight', target: 'sales-line-supply' },
        ]),
        presell('chain', '/sales-orders', [
            { kind: 'highlight', target: 'chain-step:order' },
        ]),
    ],
};
