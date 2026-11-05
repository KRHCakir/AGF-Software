// Hoofdstuk "Voorraad en verkopen voor aankomst": in de AGF verkoop je vóór de goederen binnen zijn. Eerst Beschikbaarheid (Voorraad,
// Verwacht, Verkocht, Vrij te verkopen), dan met Verkopen meer verkopen dan vrij (uit verwachte aanvoer, tekort, geen blokkade), dan de
// open demo-inkooporder ontvangen en goedkeuren (dezelfde stappen als Ontvangst en keuring), dan in de werklijst "Verkocht, wacht op
// voorraad" de regel reserveren met "oudste partij eerst". Het tekort blijft staan tot er meer inkoop is: zo werkt het model.

import { prerequisite, step } from './compose';
import { CUSTOMER_STEPS } from './masterdataChapter';
import { lookupPresell, precheckPresell } from './prechecks';
import { OPEN_ORDER_FOR_RECEIPT, RECEIVE_CORE } from './purchaseChapters';
import type { TourAction, TourChapter } from './types';

const presell = (id: string, route: string, actions: readonly TourAction[]) => step('presell', id, route, actions);

export const PRESELL_CHAPTER: TourChapter = {
    id: 'presell',
    titleKey: 'tour_ch_presell_title',
    summaryKey: 'tour_ch_presell_summary',
    doneKey: 'tour_ch_presell_done',
    writes: true,
    precheck: precheckPresell,
    steps: [
        ...OPEN_ORDER_FOR_RECEIPT,
        ...prerequisite(CUSTOMER_STEPS, 'mc', '{{needCustomer}}'),
        presell('free', '/availability?productVariantId={{variantId}}', [
            { kind: 'lookup', run: lookupPresell },
            { kind: 'click', target: 'availability-view-sales' },
            { kind: 'wait', target: 'availability-free:{{variantId}}' },
            { kind: 'highlight', target: 'availability-free:{{variantId}}' },
        ]),
        // Verkopen bij het artikel: een nieuwe verkooporder met dit artikel en deze eenheid. Meer dan vrij: uit verwachte aanvoer, tekort.
        presell('sell', '/availability?productVariantId={{variantId}}', [
            { kind: 'click', target: 'availability-sell:{{variantId}}' },
            { kind: 'pick', target: 'sales-customer', text: '{{customerSearch}}', expect: '{{customerName}}' },
            { kind: 'type', target: 'sales-reference', text: '{{tourRef}}' },
            { kind: 'type', target: 'sales-delivery', text: '{{deliveryDate}}' },
            { kind: 'type', target: 'sales-line-quantity:0', text: '{{sellQuantity}}' },
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
        // Ontvangen en goedkeuren van de open demo-inkooporder: exact de stappen (en teksten) van Ontvangst en keuring.
        ...RECEIVE_CORE,
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
