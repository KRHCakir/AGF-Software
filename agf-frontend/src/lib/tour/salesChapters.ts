// Hoofdstuk Verkooporder, reserveren en verzenden; Inkoopfactuur invoeren; en de lege plek voor verkoopfacturen, die volgen zodra ze er
// zijn. Op een lege database maakt Verkooporder eerst zelf stamgegevens, een inkooporder en goedgekeurde voorraad aan.

import { prerequisite, step } from './compose';
import { CUSTOMER_STEPS } from './masterdataChapter';
import { lookupCosts, lookupInvoice, lookupSales, precheckCosts, precheckInvoice, precheckSales } from './prechecks';
import { MASTER_FOR_PURCHASE, ORDER_CORE, RECEIVE_CORE } from './purchaseChapters';
import type { TourAction, TourChapter, TourStep } from './types';

const sales = (id: string, actions: readonly TourAction[]) => step('sales', id, '/sales-orders', actions);

// Klant, leverancier, artikel, inkooporder en goedgekeurde voorraad, voor zover ze er nog niet zijn.
export const STOCK_FOR_SALES: TourStep[] = [
    ...MASTER_FOR_PURCHASE,
    ...prerequisite(CUSTOMER_STEPS, 'mc', '{{needCustomer}}'),
    ...prerequisite(ORDER_CORE, 'o', '{{needOrder}}'),
    ...prerequisite(RECEIVE_CORE, 'r', '{{needStock}}'),
];

// Een verkooporder uit voorraad: klant, klantreferentie, leverdatum, regel; opslaan (VO-nummer); voorraad kiezen en reserveren (per stuk
// de pallet, partij en inkooporder); verzenden.
export const SALES_CORE: TourStep[] = [
    sales('new', [
        { kind: 'lookup', run: lookupSales },
        { kind: 'click', target: 'sales-new' },
        { kind: 'highlight', target: 'sales-customer' },
    ]),
    sales('customer', [
        { kind: 'pick', target: 'sales-customer', text: '{{customerSearch}}', expect: '{{customerName}}' },
        { kind: 'type', target: 'sales-reference', text: '{{tourRef}}' },
        { kind: 'type', target: 'sales-delivery', text: '{{deliveryDate}}' },
    ]),
    // De verkoop-partij: voorgesteld bij een nieuwe order, naast het VO-nummer; de rondleiding laat het voorstel staan.
    sales('partij', [{ kind: 'highlight', target: 'sales-partij' }]),
    sales('line', [
        { kind: 'pick', target: 'sales-line-product-0', text: '{{productCode}}' },
        { kind: 'type', target: 'sales-line-quantity:0', text: '{{salesQuantity}}' },
        { kind: 'highlight', target: 'sales-line-kg:0' },
    ]),
    // Emballage: een pallet bij de goederen, met het voorstel uit de regels; doet niets met de voorraad en komt later op de factuur.
    sales('packaging', [
        { kind: 'click', target: 'sales-add-pallet' },
        { kind: 'highlight', target: 'sales-packaging-line:0' },
    ]),
    sales('save', [
        { kind: 'click', target: 'sales-save' },
        { kind: 'wait', target: 'sales-saved-number' },
        { kind: 'remember', target: 'sales-saved-number', name: 'salesNumber', pattern: /(VO-\d{4}-\d+)/ },
        { kind: 'highlight', target: 'sales-saved-number' },
        { kind: 'click', target: 'sales-open-saved' },
        { kind: 'wait', target: 'sales-detail:{{tourRef}}' },
    ]),
    sales('stock', [
        { kind: 'click', target: 'sales-choose-stock' },
        { kind: 'wait', target: 'sales-stock:fit' },
        { kind: 'highlight', target: 'sales-stock:fit' },
        { kind: 'click', target: 'sales-stock:fit' },
    ]),
    sales('reserve', [
        { kind: 'type', target: 'sales-reserve-quantity', text: '{{salesQuantity}}' },
        { kind: 'click', target: 'sales-reserve-confirm' },
        { kind: 'wait', target: 'sales-line-state:reserved' },
        { kind: 'highlight', target: 'sales-line-state:reserved' },
    ]),
    sales('origin', [
        { kind: 'wait', target: 'sales-reservations' },
        { kind: 'highlight', target: 'sales-reservations' },
    ]),
    sales('dispatch', [
        { kind: 'click', target: 'sales-dispatch' },
        { kind: 'click', target: 'sales-dispatch-all' },
        { kind: 'click', target: 'sales-dispatch-save' },
        { kind: 'wait', target: 'sales-dispatch-done' },
        { kind: 'highlight', target: 'sales-dispatch-done' },
    ]),
];

export const SALES_CHAPTER: TourChapter = {
    id: 'sales',
    titleKey: 'tour_ch_sales_title',
    summaryKey: 'tour_ch_sales_summary',
    doneKey: 'tour_ch_sales_done',
    writes: true,
    precheck: precheckSales,
    steps: [...STOCK_FOR_SALES, ...SALES_CORE, sales('summary', [{ kind: 'highlight', target: 'sales-detail:{{tourRef}}' }])],
};

const invoice = (id: string, actions: readonly TourAction[]) => step('pinvoice', id, '/purchase-invoices/new', actions);

// Inkoopfactuur invoeren: de factuur van de leverancier van de rondleiding, één regel met het rondleidingsartikel; het bedrag staat
// daarna met twee decimalen (12 wordt 12,00) en de totalen rekenen mee.
export const PURCHASE_INVOICE_CHAPTER: TourChapter = {
    id: 'purchase-invoice',
    titleKey: 'tour_ch_pinvoice_title',
    summaryKey: 'tour_ch_pinvoice_summary',
    doneKey: 'tour_ch_pinvoice_done',
    writes: true,
    precheck: precheckInvoice,
    steps: [
        ...MASTER_FOR_PURCHASE,
        invoice('vendor', [
            { kind: 'lookup', run: lookupInvoice },
            { kind: 'pick', target: 'pinvoice-vendor', text: '{{vendorSearch}}', expect: '{{vendorName}}' },
            { kind: 'type', target: 'pinvoice-number', text: 'F-{{tourRef}}' },
            { kind: 'highlight', target: 'pinvoice-due-date' },
        ]),
        invoice('line', [
            { kind: 'pick', target: 'pinvoice-line-item-0', text: '{{productCode}}' },
            { kind: 'type', target: 'pinvoice-line-quantity:0', text: '10' },
            { kind: 'type', target: 'pinvoice-line-price:0', text: '12' },
            { kind: 'click', target: 'pinvoice-number' },
            // Zonder decimalen: het bedrag staat als 120,00 (nl) of 120.00 (en) op het scherm, en de controle zoekt een deel van de tekst.
            { kind: 'assert', target: 'pinvoice-line-amount:0', text: '120' },
            { kind: 'highlight', target: 'pinvoice-totals' },
        ]),
        invoice('save', [
            { kind: 'click', target: 'pinvoice-save' },
            { kind: 'wait', target: 'pinvoice-saved' },
            { kind: 'highlight', target: 'pinvoice-saved' },
        ]),
    ],
};

const costs = (id: string, route: string, actions: readonly TourAction[]) => step('costs', id, route, actions);

// Kosten verdelen over partijen: een directe factuur van derden (zonder inkooporder) met één regel Handling van € 5.000; daarvan gaat
// € 500 naar de verkoop-partij van de rondleiding en de rest blijft algemene kosten. Daarna staat het bedrag bij Kosten op de partij.
// Op een lege database maakt het hoofdstuk eerst stamgegevens, voorraad en een verkoop aan (zoals De rode draad).
export const COSTS_CHAPTER: TourChapter = {
    id: 'costs',
    titleKey: 'tour_ch_costs_title',
    summaryKey: 'tour_ch_costs_summary',
    doneKey: 'tour_ch_costs_done',
    writes: true,
    precheck: precheckCosts,
    steps: [
        ...STOCK_FOR_SALES,
        ...prerequisite(SALES_CORE, 's', '{{needSale}}'),
        costs('invoice', '/purchase-invoices/new', [
            { kind: 'lookup', run: lookupCosts },
            { kind: 'wait', target: 'purchase-direct-hint' },
            { kind: 'highlight', target: 'purchase-direct-hint' },
            { kind: 'pick', target: 'pinvoice-vendor', text: '{{vendorSearch}}', expect: '{{vendorName}}' },
            { kind: 'type', target: 'pinvoice-number', text: 'K-{{tourRef}}' },
        ]),
        costs('line', '/purchase-invoices/new', [
            { kind: 'pick', target: 'pinvoice-line-item-0', text: 'Handling' },
            { kind: 'type', target: 'pinvoice-line-price:0', text: '5000' },
            { kind: 'click', target: 'pinvoice-number' },
            { kind: 'assert', target: 'pinvoice-line-amount:0', text: '5' },
        ]),
        costs('save', '/purchase-invoices/new', [
            { kind: 'click', target: 'pinvoice-save' },
            { kind: 'wait', target: 'pinvoice-saved' },
            { kind: 'click', target: 'pinvoice-allocate' },
            { kind: 'wait', target: 'cost-allocation' },
        ]),
        costs('choose', '/purchase-invoices', [
            { kind: 'click', target: 'allocation-mode:Sales' },
            { kind: 'type', target: 'allocation-search', text: '{{salesPartijNumber}}' },
            { kind: 'click', target: 'allocation-pick:{{salesPartijNumber}}' },
            { kind: 'type', target: 'allocation-amount:{{salesPartijNumber}}', text: '500' },
            { kind: 'highlight', target: 'allocation-summary' },
        ]),
        costs('allocate', '/purchase-invoices', [
            { kind: 'click', target: 'allocation-save' },
            { kind: 'wait', target: 'allocation-saved' },
            { kind: 'highlight', target: 'allocation-saved' },
        ]),
        costs('partij', '/partijen?id={{salesPartijId}}', [
            { kind: 'wait', target: 'partij-costs' },
            { kind: 'highlight', target: 'partij-cost-kind:Handling' },
        ]),
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
