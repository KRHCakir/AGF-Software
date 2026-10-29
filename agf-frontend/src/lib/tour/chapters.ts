// De hoofdstukken van de rondleiding, in volgorde. Elke nieuwe functie die een scherm toevoegt of wijzigt, neemt het
// bijbehorende hoofdstuk mee (met de data-tour-attributen en de test); zie docs/demo-tour.md.

import { ORDER_CHAPTER, RECEIVE_CHAPTER } from './purchaseChapters.ts';
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
        screen('supply', '/supply'),
        screen('purchase', '/purchase-orders'),
        screen('inspection', '/inspection'),
        screen('availability', '/availability'),
        screen('sales', '/sales-orders'),
    ],
};

export const TOUR_CHAPTERS: readonly TourChapter[] = [SCREENS_CHAPTER, ORDER_CHAPTER, RECEIVE_CHAPTER, SALES_CHAPTER, INVOICE_CHAPTER];
