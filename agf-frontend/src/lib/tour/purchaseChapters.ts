// Hoofdstuk Inkooporder aanmaken, Ontvangst en keuring, en Afwijking bij ontvangst. Alleen data-tour-namen en vaste teksten; de
// voorcontrole zegt wat er al is, de stappen lezen de waarden die per database verschillen (zoekcode, snelcode, de open demo-order).
// Ontbreekt er iets (lege database), dan maakt het hoofdstuk het eerst zelf aan met de stappen van Stamgegevens en Inkooporder aanmaken.

import { prerequisite, step } from './compose';
import { ARTICLE_STEPS, VENDOR_STEPS } from './masterdataChapter';
import { lookupOrderEdit, lookupOtherArticle, lookupPurchase, lookupReceive, lookupSplit, precheckOrderEdit, precheckPurchase, precheckReceive, precheckSplit } from './prechecks';
import type { TourAction, TourChapter, TourStep } from './types';

const order = (id: string, route: string, actions: readonly TourAction[]) => step('order', id, route, actions);

// Leverancier en artikel van de rondleiding, als ze er nog niet zijn.
export const MASTER_FOR_PURCHASE: TourStep[] = [...prerequisite(VENDOR_STEPS, 'mv', '{{needVendor}}'), ...prerequisite(ARTICLE_STEPS, 'ma', '{{needArticle}}')];

// De inkooporder zelf: leverancier, haar ordernummer en de datums, partij, één regel met Tab door artikel, aantal en prijs (eenheid, kg, merk,
// land, maat en inhoud komen uit het artikel), de knop Bruto aanpassen en de totalen onder de kolommen, en opslaan.
export const ORDER_CORE: TourStep[] = [
    order('open', '/purchase-orders/new', [
        { kind: 'lookup', run: lookupPurchase },
        { kind: 'highlight', target: 'supply-vendor' },
    ]),
    order('vendor', '/purchase-orders/new', [{ kind: 'pick', target: 'supply-vendor', text: '{{vendorSearch}}', expect: '{{vendorName}}' }]),
    // Direct na de leverancier haar ordernummer (het hoofdnummer van de order, DEMO-TOUR-…), dan de datums: een tweecijferig jaar wordt bij
    // het verlaten dd-mm-jjjj.
    order('reference', '/purchase-orders/new', [
        { kind: 'type', target: 'supply-reference', text: '{{tourRef}}' },
        { kind: 'highlight', target: 'supply-ordered-on' },
        { kind: 'type', target: 'supply-expected', text: '{{expectedDate}}' },
        // Valuta-ronde: de valuta staat op euro; bij een andere valuta komt de ECB-koers er vanzelf bij.
        { kind: 'highlight', target: 'supply-currency' },
    ]),
    // Partij: het voorgestelde nummer blijft staan (staat de nummering op Eigen, dan typt de rondleiding haar eigen kenmerk). Een tweede
    // order op dezelfde partij kies je met "Bestaande partij kiezen". Daarnaast het lot van de leverancier.
    order('partij', '/purchase-orders/new', [
        { kind: 'highlight', target: 'supply-partij' },
        { kind: 'type', target: 'supply-partij', text: '{{tourRef}}', when: '{{ownNumbering}}' },
        { kind: 'highlight', target: 'supply-partij-choose' },
        { kind: 'type', target: 'supply-supplier-lot', text: 'LOT-{{tourRef}}' },
    ]),
    // Feedback 22 november: "Artikel toevoegen" is een lijst van bestaande artikelen; de keuze staat direct op de regel, cursor op Aantal.
    order('article', '/purchase-orders/new', [
        { kind: 'click', target: 'supply-article-add' },
        { kind: 'type', target: 'article-add-search', text: '{{productCode}}' },
        { kind: 'click', target: 'article-add-option:{{productCode}}' },
        { kind: 'assert', target: 'supply-expected', value: '{{expectedDateFull}}' },
        { kind: 'type', target: 'supply-line-quantity:0', text: '{{quantity}}' },
        { kind: 'type', target: 'supply-line-price:0', text: '12,50' },
        // Certificaten deel 2: het GGN van het product zoals op de factuur van de leverancier (hier dat van de teler).
        { kind: 'type', target: 'supply-line-ggn:0', text: '4049928415684' },
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
        // Sorteren (feedback 22 november): alleen aanwijzen, niet klikken; een klik zou de gekozen sortering in deze browser onthouden.
        order('sort', '/purchase-orders', [
            { kind: 'highlight', target: 'sort-header:vendor' },
        ]),
        order('find', '/purchase-orders', [
            // Terugvinden op het ordernummer van de leverancier; de titel toont dat nummer met het eigen IO-nummer klein eronder.
            { kind: 'type', target: 'po-search', text: '{{tourRef}}' },
            { kind: 'click', target: 'po-open:{{orderNumber}}' },
            { kind: 'assert', target: 'po-detail-number', text: '{{tourRef}}' },
            { kind: 'assert', target: 'po-detail-number', text: '{{orderNumber}}' },
        ]),
        // Factuur koppelen (stap 4b): "Factuur invoeren" op de order opent de inkoopfactuur met de leverancier en de regels van de bon
        // (aantal en prijs voorgevuld). De factuur zegt 12,00 in plaats van 12,50: het verschil staat in rood en bij opslaan komt eenmaal
        // de vraag; Bon aanpassen zet de factuurprijs op de order (met auditregel) en de order wordt definitief.
        order('invoice', '/purchase-orders', [
            { kind: 'highlight', target: 'po-invoice-enter' },
            { kind: 'click', target: 'po-invoice-enter' },
            // In dezelfde stap wachten tot de regels uit de bon er staan: anders navigeert de volgende stap al naar de lege invoer.
            { kind: 'wait', target: 'pinvoice-order-info:0' },
        ]),
        order('invoice-lines', '/purchase-invoices/new', [
            { kind: 'highlight', target: 'pinvoice-order-info:0' },
            { kind: 'type', target: 'pinvoice-number', text: 'F-{{tourRef}}' },
            { kind: 'type', target: 'pinvoice-line-price:0', text: '12,00' },
            { kind: 'wait', target: 'pinvoice-order-difference:0' },
            { kind: 'highlight', target: 'pinvoice-order-difference:0' },
        ]),
        order('invoice-question', '/purchase-invoices/new', [
            { kind: 'click', target: 'pinvoice-save' },
            { kind: 'wait', target: 'pinvoice-price-question' },
            { kind: 'highlight', target: 'pinvoice-adjust-order' },
            { kind: 'click', target: 'pinvoice-adjust-order' },
            { kind: 'wait', target: 'pinvoice-saved' },
        ]),
        order('by-invoice', '/purchase-orders', [
            { kind: 'type', target: 'po-search', text: 'F-{{tourRef}}' },
            { kind: 'wait', target: 'po-open:{{orderNumber}}' },
            { kind: 'click', target: 'po-open:{{orderNumber}}' },
            { kind: 'wait', target: 'po-linked-invoice:F-{{tourRef}}' },
            { kind: 'highlight', target: 'po-linked-invoice:F-{{tourRef}}' },
            { kind: 'wait', target: 'po-definitive' },
            { kind: 'highlight', target: 'po-definitive' },
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

const orderEdit = (id: string, route: string, actions: readonly TourAction[]) => step('orderedit', id, route, actions);

// Order aanpassen en definitief maken (stap 4a): op een open demo-order Aanpassen (hetzelfde formulier als aanmaken), prijs en notitie
// wijzigen en opslaan; de wijziging staat in de geschiedenis. Dan Definitief maken (vraag op de pagina) en een correctie met reden.
export const ORDER_EDIT_CHAPTER: TourChapter = {
    id: 'order-edit',
    titleKey: 'tour_ch_orderedit_title',
    summaryKey: 'tour_ch_orderedit_summary',
    doneKey: 'tour_ch_orderedit_done',
    writes: true,
    precheck: precheckOrderEdit,
    steps: [
        ...MASTER_FOR_PURCHASE,
        ...prerequisite(ORDER_CORE, 'o', '{{needOrder}}'),
        orderEdit('open', '/purchase-orders', [
            { kind: 'lookup', run: lookupOrderEdit },
            { kind: 'navigate', href: '/purchase-orders?id={{orderId}}' },
            { kind: 'wait', target: 'po-edit' },
            { kind: 'highlight', target: 'po-edit' },
            { kind: 'click', target: 'po-edit' },
        ]),
        orderEdit('edit', '/purchase-orders/edit?id={{orderId}}', [
            { kind: 'wait', target: 'supply-line-price:0' },
            { kind: 'type', target: 'supply-line-price:0', text: '13,25' },
            { kind: 'type', target: 'supply-note', text: 'Prijs aangepast na telefoontje ({{orderRef}})' },
            { kind: 'click', target: 'supply-save' },
        ]),
        orderEdit('history', '/purchase-orders?id={{orderId}}', [
            { kind: 'wait', target: 'po-changes' },
            { kind: 'highlight', target: 'po-changes' },
        ]),
        orderEdit('finalize', '/purchase-orders?id={{orderId}}', [
            { kind: 'click', target: 'po-finalize' },
            { kind: 'highlight', target: 'po-finalize-yes' },
            { kind: 'click', target: 'po-finalize-yes' },
            { kind: 'wait', target: 'po-definitive' },
            { kind: 'highlight', target: 'po-definitive' },
        ]),
        orderEdit('correct', '/purchase-orders?id={{orderId}}', [
            { kind: 'click', target: 'po-correct' },
            { kind: 'type', target: 'po-correction-price:{{lineId}}', text: '13,40' },
            { kind: 'type', target: 'po-correction-reason', text: 'Prijsafspraak na levering' },
            { kind: 'click', target: 'po-correction-save' },
            { kind: 'wait', target: 'po-correct' },
            { kind: 'highlight', target: 'po-changes' },
        ]),
    ],
};

// Stamgegevens en een open demo-inkooporder, als ze er nog niet zijn (voor de hoofdstukken die ontvangen).
export const OPEN_ORDER_FOR_RECEIPT: TourStep[] = [...MASTER_FOR_PURCHASE, ...prerequisite(ORDER_CORE, 'o', '{{needOrder}}')];

const receive = (id: string, route: string, actions: readonly TourAction[]) => step('receive', id, route, actions);

// Ontvangen en goedkeuren van de open demo-inkooporder; ook gebruikt door Verkopen voor aankomst en Verkooporder. Feedback 22 november:
// één scherm met Dit wordt verwacht en per regel één knop Ontvangen; geen controlevragen. Keuren is de optionele knop Keuren / afwijzen.
export const RECEIVE_CORE: TourStep[] = [
    receive('open', '/receipts', [
        { kind: 'lookup', run: lookupReceive },
        { kind: 'type', target: 'receive-search', text: '{{orderId}}' },
        { kind: 'click', target: 'receive-order:{{orderId}}' },
        { kind: 'wait', target: 'receive-expected' },
        { kind: 'highlight', target: 'receive-expected' },
    ]),
    receive('line', '/receipts?order={{orderId}}', [
        { kind: 'type', target: 'receive-quantity:{{lineId}}', text: '{{quantity}}' },
        { kind: 'highlight', target: 'receive-line-pallets:{{lineId}}' },
    ]),
    receive('register', '/receipts?order={{orderId}}', [
        { kind: 'click', target: 'receive-line-button:{{lineId}}' },
        { kind: 'wait', target: 'receive-line-inspect:{{lineId}}' },
        { kind: 'highlight', target: 'receive-notice' },
    ]),
    receive('inspector', '/receipts?order={{orderId}}', [
        { kind: 'click', target: 'receive-line-inspect:{{lineId}}' },
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

const split = (id: string, route: string, actions: readonly TourAction[]) => step('split', id, route, actions);

// Het artikel uit het andere land (zelfde artikel, maat, verpakking, klasse en merk; alleen het land verschilt), als het er nog niet is: de
// stappen van Nieuw artikel, met de snelcode, het land en het merk van het bestelde artikel.
const OTHER_ARTICLE_STEPS: TourStep[] = ARTICLE_STEPS.map((original, index) => ({
    ...original,
    actions: [
        ...(index === 0 ? [{ kind: 'lookup', run: lookupOtherArticle } as TourAction] : []),
        ...original.actions.map((action): TourAction => {
            if (action.kind === 'type' && action.target === 'article-shortcut-code') return { ...action, text: '{{otherShortcut}}' };
            if (action.kind === 'type' && action.target === 'article-brand') return { ...action, text: '{{orderedBrand}}' };
            if (action.kind === 'pick' && action.target === 'article-origin') return { ...action, text: '{{otherCode}}', expect: '{{otherOrigin}}' };
            return action;
        }),
    ],
}));

// Feedback 22 november: iets anders ontvangen. De helft komt binnen zoals besteld; de rest blijkt uit een ander land te komen. De gebruiker
// past de orderregel aan (geen controlevraag bij ontvangst): omdat er al iets ontvangen is, splitst de regel, en het restant van de oude
// regel vervalt, zodat niets dubbel telt.
export const SPLIT_CHAPTER: TourChapter = {
    id: 'split',
    titleKey: 'tour_ch_split_title',
    summaryKey: 'tour_ch_split_summary',
    doneKey: 'tour_ch_split_done',
    writes: true,
    precheck: precheckSplit,
    steps: [
        ...OPEN_ORDER_FOR_RECEIPT,
        ...prerequisite(OTHER_ARTICLE_STEPS, 'mz', '{{needOtherArticle}}'),
        split('receive', '/receipts', [
            { kind: 'lookup', run: lookupSplit },
            { kind: 'type', target: 'receive-search', text: '{{orderId}}' },
            { kind: 'click', target: 'receive-order:{{orderId}}' },
            { kind: 'type', target: 'receive-quantity:{{lineId}}', text: '{{half}}' },
            { kind: 'click', target: 'receive-line-button:{{lineId}}' },
            { kind: 'wait', target: 'receive-line-inspect:{{lineId}}' },
            { kind: 'highlight', target: 'receive-line-received:{{lineId}}' },
        ]),
        split('article', '/purchase-orders/edit?id={{orderId}}', [
            { kind: 'click', target: 'supply-line-product-{{lineIndex}}-clear' },
            { kind: 'pick', target: 'supply-line-product-{{lineIndex}}', text: '{{otherSearch}}', expect: '{{otherOrigin}}' },
            { kind: 'highlight', target: 'supply-line-split:{{lineIndex}}' },
        ]),
        split('ask', '/purchase-orders/edit?id={{orderId}}', [
            { kind: 'click', target: 'supply-save' },
            { kind: 'wait', target: 'split-dialog' },
            { kind: 'highlight', target: 'split-dialog' },
        ]),
        split('save', '/purchase-orders/edit?id={{orderId}}', [
            { kind: 'click', target: 'split-remainder:{{lineIndex}}-Cancelled' },
            { kind: 'click', target: 'split-confirm' },
            { kind: 'wait', target: 'po-line-split' },
        ]),
        split('order', '/purchase-orders?id={{orderId}}', [
            { kind: 'wait', target: 'order-line-progress' },
            { kind: 'highlight', target: 'po-line-progress' },
        ]),
    ],
};
