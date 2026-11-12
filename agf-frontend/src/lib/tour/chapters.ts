// De hoofdstukken van de rondleiding, in volgorde. Elke nieuwe functie die een scherm toevoegt of wijzigt, neemt het
// bijbehorende hoofdstuk mee (met de data-tour-attributen en de test); zie docs/demo-tour.md.

import { prerequisite, step } from './compose';
import { MASTERDATA_CHAPTER } from './masterdataChapter';
import { AI_INVOICE_CHAPTER } from './aiInvoiceChapter';
import { DEVIATION_CHAPTER, ORDER_CHAPTER, RECEIVE_CHAPTER } from './purchaseChapters';
import { lookupChain, precheckChain } from './prechecks';
import { COSTS_CHAPTER, INVOICE_CHAPTER, PURCHASE_INVOICE_CHAPTER, SALES_CHAPTER, SALES_CORE, STOCK_FOR_SALES } from './salesChapters';
import { PRESELL_CHAPTER } from './presellChapter';
import type { TourChapter, TourStep } from './types';

const screen = (id: string, route: string): TourStep => step('screens', id, route, [{ kind: 'highlight', target: 'page-title' }]);

// Kennismaking: langs de vier groepen van het menu (Inkoop, Voorraad, Verkoop, Stamgegevens), alleen navigeren en aanwijzen, nooit iets
// invullen (werkt ook zonder NEXT_PUBLIC_DEMO_TOUR).
export const SCREENS_CHAPTER: TourChapter = {
    id: 'screens',
    titleKey: 'tour_ch_screens_title',
    summaryKey: 'tour_ch_screens_summary',
    writes: false,
    steps: [
        screen('purchase', '/purchase-orders'),
        screen('supply', '/purchase-orders/new'),
        screen('receipts', '/receipts'),
        screen('inspection', '/inspection'),
        screen('availability', '/availability'),
        screen('partijen', '/partijen'),
        screen('sales', '/sales-orders'),
        screen('direct-trade', '/direct-trade'),
        screen('relations', '/relations'),
        screen('articles', '/articles'),
    ],
};

const chain = (id: string, route: string, actions: TourStep['actions']): TourStep => step('chain', id, route, actions);

// De rode draad van één order: de strip op de inkooporder (waar staat hij, waar ging het heen), op de ontvangst en op de verkooporder
// (waar komt het vandaan), en de partij onder Voorraad › Partijen. Alleen kijken; is er nog geen demo-order waaruit verkocht is (lege
// database), dan maakt het hoofdstuk eerst stamgegevens, een order, voorraad en een verkoop aan.
export const CHAIN_CHAPTER: TourChapter = {
    id: 'chain',
    titleKey: 'tour_ch_chain_title',
    summaryKey: 'tour_ch_chain_summary',
    doneKey: 'tour_ch_chain_done',
    writes: false,
    precheck: precheckChain,
    steps: [
        ...STOCK_FOR_SALES,
        ...prerequisite(SALES_CORE, 's', '{{needSale}}'),
        chain('strip', '/purchase-orders?id={{orderId}}', [
            { kind: 'lookup', run: lookupChain },
            { kind: 'wait', target: 'chain-strip' },
            { kind: 'highlight', target: 'chain-strip' },
        ]),
        chain('progress', '/purchase-orders?id={{orderId}}', [{ kind: 'highlight', target: 'chain-step:inspection' }]),
        chain('sold', '/purchase-orders?id={{orderId}}', [{ kind: 'highlight', target: 'chain-step:sales' }]),
        chain('receipt', '/receipts?order={{orderId}}', [
            { kind: 'wait', target: 'chain-step:receipt' },
            { kind: 'highlight', target: 'chain-step:receipt' },
        ]),
        chain('origin', '/sales-orders?orderId={{salesOrderId}}', [{ kind: 'wait', target: 'chain-step:order' }, { kind: 'highlight', target: 'chain-step:order' }]),
        chain('partij', '/partijen?id={{partijId}}', [
            { kind: 'wait', target: 'partij-detail' },
            { kind: 'highlight', target: 'partij-totals' },
            { kind: 'highlight', target: 'partij-order:{{orderNumber}}' },
        ]),
    ],
};

export const TOUR_CHAPTERS: readonly TourChapter[] = [
    SCREENS_CHAPTER, MASTERDATA_CHAPTER, ORDER_CHAPTER, RECEIVE_CHAPTER, DEVIATION_CHAPTER, PRESELL_CHAPTER, SALES_CHAPTER, CHAIN_CHAPTER,
    PURCHASE_INVOICE_CHAPTER, AI_INVOICE_CHAPTER, COSTS_CHAPTER, INVOICE_CHAPTER,
];
