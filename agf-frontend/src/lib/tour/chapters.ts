// De hoofdstukken van de rondleiding, in volgorde. Elke nieuwe functie die een scherm toevoegt of wijzigt, neemt het
// bijbehorende hoofdstuk mee (met de data-tour-attributen en de test); zie docs/demo-tour.md.

import { DEVIATION_CHAPTER, ORDER_CHAPTER, RECEIVE_CHAPTER } from './purchaseChapters.ts';
import { precheckChain } from './prechecks.ts';
import { INVOICE_CHAPTER, SALES_CHAPTER } from './salesChapters.ts';
import type { TourChapter, TourStep } from './types.ts';

const screen = (id: string, route: string): TourStep => ({
    id,
    route,
    titleKey: `tour_screens_${id}_title`,
    whatKey: `tour_screens_${id}_what`,
    whyKey: `tour_screens_${id}_why`,
    actions: [{ kind: 'highlight', target: 'page-title' }],
});

// Kennismaking: alleen navigeren en aanwijzen, nooit iets invullen (werkt ook zonder NEXT_PUBLIC_DEMO_TOUR).
export const SCREENS_CHAPTER: TourChapter = {
    id: 'screens',
    titleKey: 'tour_ch_screens_title',
    summaryKey: 'tour_ch_screens_summary',
    writes: false,
    steps: [
        screen('purchase', '/purchase-orders'),
        screen('supply', '/purchase-orders/new'),
        screen('inspection', '/inspection'),
        screen('availability', '/availability'),
        screen('sales', '/sales-orders'),
    ],
};

const chain = (id: string, route: string, actions: TourStep['actions']): TourStep => ({
    id, route, titleKey: `tour_chain_${id}_title`, whatKey: `tour_chain_${id}_what`, whyKey: `tour_chain_${id}_why`, actions,
});

// De keten van één order: de strip bovenaan de inkooporder (waar staat hij, waar ging het heen) en op de verkooporder (waar komt het
// vandaan). Alleen kijken; vraagt een demo-order waaruit al verkocht is.
export const CHAIN_CHAPTER: TourChapter = {
    id: 'chain',
    titleKey: 'tour_ch_chain_title',
    summaryKey: 'tour_ch_chain_summary',
    doneKey: 'tour_ch_chain_done',
    writes: false,
    precheck: precheckChain,
    steps: [
        chain('strip', '/purchase-orders?id={{orderId}}', [{ kind: 'wait', target: 'chain-strip' }, { kind: 'highlight', target: 'chain-strip' }]),
        chain('progress', '/purchase-orders?id={{orderId}}', [{ kind: 'highlight', target: 'chain-step:inspection' }]),
        chain('sold', '/purchase-orders?id={{orderId}}', [{ kind: 'highlight', target: 'chain-step:sales' }]),
        chain('origin', '/sales-orders?orderId={{salesOrderId}}', [{ kind: 'wait', target: 'chain-step:order' }, { kind: 'highlight', target: 'chain-step:order' }]),
    ],
};

export const TOUR_CHAPTERS: readonly TourChapter[] = [SCREENS_CHAPTER, ORDER_CHAPTER, RECEIVE_CHAPTER, DEVIATION_CHAPTER, SALES_CHAPTER, CHAIN_CHAPTER, INVOICE_CHAPTER];
