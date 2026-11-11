// Hoofdstuk Inkooporder aanmaken, Ontvangst en keuring, en Afwijking bij ontvangst. Alleen data-tour-namen en vaste teksten; de
// voorcontrole zegt wat er al is, de stappen lezen de waarden die per database verschillen (zoekcode, snelcode, de open demo-order).
// Ontbreekt er iets (lege database), dan maakt het hoofdstuk het eerst zelf aan met de stappen van Stamgegevens en Inkooporder aanmaken.

import { prerequisite, step } from './compose';
import { ARTICLE_STEPS, VENDOR_STEPS } from './masterdataChapter';
import { lookupDeviation, lookupDeviationStock, lookupPurchase, lookupReceive, precheckDeviation, precheckPurchase, precheckReceive } from './prechecks';
import type { TourAction, TourChapter, TourStep } from './types';

const order = (id: string, route: string, actions: readonly TourAction[]) => step('order', id, route, actions);

// Leverancier en artikel van de rondleiding, als ze er nog niet zijn.
export const MASTER_FOR_PURCHASE: TourStep[] = [...prerequisite(VENDOR_STEPS, 'mv', '{{needVendor}}'), ...prerequisite(ARTICLE_STEPS, 'ma', '{{needArticle}}')];

// De inkooporder zelf: leverancier, partij, referentie en datum, één regel met Tab door artikel, aantal en prijs (eenheid, kg, merk,
// land, maat en inhoud komen uit het artikel), de knop Bruto aanpassen en de totalen onder de kolommen, en opslaan.
export const ORDER_CORE: TourStep[] = [
    order('open', '/purchase-orders/new', [
        { kind: 'lookup', run: lookupPurchase },
        { kind: 'highlight', target: 'supply-vendor' },
    ]),
    order('vendor', '/purchase-orders/new', [{ kind: 'pick', target: 'supply-vendor', text: '{{vendorSearch}}', expect: '{{vendorName}}' }]),
    // Partij: het voorgestelde nummer blijft staan (staat de nummering op Eigen, dan typt de rondleiding haar eigen kenmerk). Een tweede
    // order op dezelfde partij kies je met "Bestaande partij kiezen". Daarnaast de partij van de leverancier.
    order('partij', '/purchase-orders/new', [
        { kind: 'highlight', target: 'supply-partij' },
        { kind: 'type', target: 'supply-partij', text: '{{tourRef}}', when: '{{ownNumbering}}' },
        { kind: 'highlight', target: 'supply-partij-choose' },
        { kind: 'type', target: 'supply-supplier-lot', text: 'LOT-{{tourRef}}' },
    ]),
    // Datum met tweecijferig jaar: het veld maakt er bij het verlaten dd-mm-jjjj van.
    order('reference', '/purchase-orders/new', [
        { kind: 'type', target: 'supply-reference', text: '{{tourRef}}' },
        { kind: 'highlight', target: 'supply-ordered-on' },
        { kind: 'type', target: 'supply-expected', text: '{{expectedDate}}' },
    ]),
    order('article', '/purchase-orders/new', [
        { kind: 'pick', target: 'supply-line-product-0', text: '{{productCode}}' },
        { kind: 'assert', target: 'supply-expected', value: '{{expectedDateFull}}' },
        { kind: 'type', target: 'supply-line-quantity:0', text: '{{quantity}}' },
        { kind: 'type', target: 'supply-line-price:0', text: '12,50' },
        { kind: 'highlight', target: 'supply-line-gross-adjust:0' },
        { kind: 'highlight', target: 'supply-line-totals' },
    ]),
    order('save', '/purchase-orders/new', [
        { kind: 'click', target: 'supply-save' },
        { kind: 'wait', target: 'supply-order-number' },
        { kind: 'remember', target: 'supply-order-number', name: 'orderNumber', pattern: /(IO-\d{4}-\d+)/ },
        { kind: 'remember', target: 'supply-saved-partij-number', name: 'partijNumber', pattern: /(\S+)/ },
        { kind: 'highlight', target: 'supply-order-number' },
    ]),
];

export const ORDER_CHAPTER: TourChapter = {
    id: 'order',
    titleKey: 'tour_ch_order_title',
    summaryKey: 'tour_ch_order_summary',
    doneKey: 'tour_ch_order_done',
    writes: true,
    precheck: precheckPurchase,
    steps: [
        ...MASTER_FOR_PURCHASE,
        ...ORDER_CORE,
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
        // Voorraad › Partijen: de partij van deze order, met de order erin.
        order('lot', '/partijen', [
            { kind: 'type', target: 'partij-search', text: '{{partijNumber}}' },
            { kind: 'click', target: 'partij-open:{{partijNumber}}' },
            { kind: 'wait', target: 'partij-order:{{orderNumber}}' },
            { kind: 'highlight', target: 'partij-order:{{orderNumber}}' },
        ]),
    ],
};

// Stamgegevens en een open demo-inkooporder, als ze er nog niet zijn (voor de hoofdstukken die ontvangen).
export const OPEN_ORDER_FOR_RECEIPT: TourStep[] = [...MASTER_FOR_PURCHASE, ...prerequisite(ORDER_CORE, 'o', '{{needOrder}}')];

const receive = (id: string, route: string, actions: readonly TourAction[]) => step('receive', id, route, actions);

// Blinde invoer: wat er werkelijk is aangekomen, kenmerk voor kenmerk (lege kenmerken worden overgeslagen), daarna partij, aantal en kg.
// Heeft de orderregel een partij, dan neemt de ontvangst die over: niets te typen, de rondleiding wijst haar aan.
const enterReceipt = (lot: string): TourAction[] => [
    { kind: 'click', target: 'receive-line:{{orderId}}-{{lineId}}' },
    { kind: 'pick', target: 'receive-origin:{{lineId}}', text: '{{originText}}', expect: '{{originName}}', when: '{{originText}}' },
    { kind: 'pick', target: 'receive-size:{{lineId}}', text: '{{size}}', when: '{{size}}' },
    { kind: 'pick', target: 'receive-packaging:{{lineId}}', text: '{{packaging}}', when: '{{packaging}}' },
    { kind: 'pick', target: 'receive-class:{{lineId}}', text: '{{tradeClass}}', when: '{{tradeClass}}' },
    { kind: 'pick', target: 'receive-brand:{{lineId}}', text: '{{brand}}', when: '{{brand}}' },
    { kind: 'type', target: 'receive-lot:{{lineId}}', text: lot, when: '{{typeLot}}' },
    { kind: 'highlight', target: 'receive-partij:{{lineId}}', when: '{{partijNumber}}' },
    { kind: 'type', target: 'receive-quantity:{{lineId}}', text: '{{quantity}}' },
    { kind: 'type', target: 'receive-kg-per-package:{{lineId}}', text: '{{kgPerPackage}}', when: '{{kgPerPackage}}' },
    { kind: 'type', target: 'receive-net-kg:{{lineId}}', text: '{{netKg}}', when: '{{netKg}}' },
];

// Ontvangen en goedkeuren van de open demo-inkooporder; ook gebruikt door Verkopen voor aankomst en Verkooporder.
export const RECEIVE_CORE: TourStep[] = [
    receive('open', '/receipts', [
        { kind: 'lookup', run: lookupReceive },
        { kind: 'type', target: 'receive-search', text: '{{orderId}}' },
    ]),
    receive('line', '/receipts', [...enterReceipt('{{lot}}'), { kind: 'highlight', target: 'receive-weight:{{lineId}}' }]),
    receive('register', '/receipts', [
        { kind: 'click', target: 'receive-save' },
        { kind: 'wait', target: 'receive-to-inspection' },
        { kind: 'highlight', target: 'receive-to-inspection' },
    ]),
    receive('inspector', '/receipts', [
        { kind: 'click', target: 'receive-to-inspection' },
    ]),
    // Wie beoordeelt: per keuring gekozen (hier de rondleiding zelf); vastgelegd naast de ingelogde gebruiker.
    receive('approve', '/inspection', [
        { kind: 'click', target: 'inspection-action:Approved-{{lot}}' },
        { kind: 'type', target: 'inspection-inspector:{{lot}}', text: '{{actor}}' },
        { kind: 'click', target: 'inspection-confirm:{{lot}}' },
        { kind: 'wait', target: 'inspection-status:{{lot}}-Approved' },
        { kind: 'highlight', target: 'inspection-status:{{lot}}-Approved' },
    ]),
];

export const RECEIVE_CHAPTER: TourChapter = {
    id: 'receive',
    titleKey: 'tour_ch_receive_title',
    summaryKey: 'tour_ch_receive_summary',
    doneKey: 'tour_ch_receive_done',
    writes: true,
    precheck: precheckReceive,
    steps: [
        ...OPEN_ORDER_FOR_RECEIPT,
        ...RECEIVE_CORE,
        receive('reject', '/purchase-orders?id={{orderId}}', [
            { kind: 'click', target: 'po-reject:{{lot}}' },
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

const deviation = (id: string, route: string, actions: readonly TourAction[]) => step('deviation', id, route, actions);

// Afwijking bij ontvangst: de helft komt uit een ander land en met 5% meer kg per colli. Bestaat die variant nog niet, dan maakt de
// ontvangst hem aan; daarna vraagt "Klopt dit?" naar de herkomst en het gewicht, en komt de voorraad op de exacte variant.
export const DEVIATION_CHAPTER: TourChapter = {
    id: 'deviation',
    titleKey: 'tour_ch_deviation_title',
    summaryKey: 'tour_ch_deviation_summary',
    doneKey: 'tour_ch_deviation_done',
    writes: true,
    precheck: precheckDeviation,
    steps: [
        ...OPEN_ORDER_FOR_RECEIPT,
        deviation('open', '/receipts', [
            { kind: 'lookup', run: lookupDeviation },
            { kind: 'type', target: 'receive-search', text: '{{orderId}}' },
        ]),
        deviation('enter', '/receipts', enterReceipt('{{lot}}')),
        deviation('check', '/receipts', [
            { kind: 'click', target: 'receive-save' },
            { kind: 'click', target: 'new-article-create:{{lineId}}', when: '{{createVariant}}' },
            { kind: 'wait', target: 'deviation-dialog' },
            { kind: 'highlight', target: 'deviation-dialog' },
        ]),
        deviation('confirm', '/receipts', [
            { kind: 'select', target: 'deviation-reason', value: 'SupplierDeliveredDifferently' },
            { kind: 'click', target: 'deviation-yes' },
            { kind: 'wait', target: 'receive-to-inspection' },
        ]),
        deviation('order', '/purchase-orders?id={{orderId}}', [
            { kind: 'wait', target: 'po-deviations' },
            { kind: 'highlight', target: 'po-deviations' },
        ]),
        // De voorraad staat op de variant die werkelijk binnenkwam (in keuring tot hij is goedgekeurd).
        deviation('stock', '/availability?productVariantId={{actualVariantId}}', [
            { kind: 'lookup', run: lookupDeviationStock },
            { kind: 'click', target: 'availability-view-sales' },
            { kind: 'wait', target: 'availability-free:{{actualVariantId}}' },
            { kind: 'highlight', target: 'availability-free:{{actualVariantId}}' },
        ]),
    ],
};
