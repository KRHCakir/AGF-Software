// Hoofdstuk Stamgegevens, nodig voor een lege start: een leverancier en een klant (met adres) en een artikel (herkomst uit de landenlijst,
// maat en klasse verplicht). Alles krijgt het kenmerk van deze rondleiding ({{tourRef}}, DEMO-TOUR-…), zodat de volgende hoofdstukken het
// terugvinden. Andere hoofdstukken gebruiken deze stappen als voorwaarde (prerequisite) wanneer er nog geen rondleidingsgegevens zijn.

import { step } from './compose';
import { precheckMasterdata } from './prechecks';
import type { TourAction, TourChapter } from './types';

const master = (id: string, route: string, actions: readonly TourAction[]) => step('master', id, route, actions);

// Het adres wordt met de hand ingevuld: postcode, straat met huisnummer, plaats en land (er is nog geen opzoeking op postcode).
const address = (postalCode: string, line1: string, city: string): TourAction[] => [
    { kind: 'click', target: 'relation-section:addresses' },
    { kind: 'click', target: 'relation-address-add:Main' },
    { kind: 'type', target: 'relation-address-postal-code', text: postalCode },
    { kind: 'type', target: 'relation-address-line1', text: line1 },
    { kind: 'type', target: 'relation-address-city', text: city },
    { kind: 'type', target: 'relation-address-country-code', text: 'NL' },
];

const save = (name: string): TourAction[] => [
    { kind: 'click', target: 'relation-save' },
    { kind: 'wait', target: 'relation-notice', text: name },
    { kind: 'highlight', target: 'relation-notice' },
];

export const VENDOR_STEPS = [
    master('vendor-new', '/relations', [
        { kind: 'click', target: 'relations-new' },
        { kind: 'highlight', target: 'relation-search-code' },
    ]),
    master('vendor-identity', '/relations', [
        { kind: 'type', target: 'relation-search-code', text: '{{tourRef}}-LEV' },
        { kind: 'type', target: 'relation-name', text: '{{tourRef}} Kwekerij' },
        { kind: 'click', target: 'relation-role-vendor' },
    ]),
    master('vendor-address', '/relations', address('2991 LD', 'Handelsweg 12', 'Barendrecht')),
    master('vendor-save', '/relations', save('{{tourRef}} Kwekerij')),
];

export const CUSTOMER_STEPS = [
    master('customer-new', '/relations', [
        { kind: 'click', target: 'relations-new' },
        { kind: 'type', target: 'relation-search-code', text: '{{tourRef}}-KLANT' },
        { kind: 'type', target: 'relation-name', text: '{{tourRef}} Groothandel' },
        { kind: 'click', target: 'relation-role-customer' },
    ]),
    master('customer-address', '/relations', address('3016 DM', 'Fruitlaan 3', 'Rotterdam')),
    master('customer-save', '/relations', save('{{tourRef}} Groothandel')),
];

export const ARTICLE_STEPS = [
    master('article-new', '/articles', [
        { kind: 'click', target: 'articles-new' },
        { kind: 'type', target: 'article-shortcut-code', text: '{{tourRef}}' },
        { kind: 'type', target: 'article-product-name', text: 'Avocado' },
        { kind: 'type', target: 'article-variant-name', text: 'Hass' },
    ]),
    // Herkomst uit de landenlijst: "PE" (de landcode, of een eigen snelkoppeling) vindt Peru, Tab neemt het over.
    master('article-origin', '/articles', [
        { kind: 'pick', target: 'article-origin', text: 'PE', expect: 'Peru' },
    ]),
    master('article-size-class', '/articles', [
        { kind: 'type', target: 'article-size-category', text: '18' },
        { kind: 'pick', target: 'article-class', text: 'I' },
        { kind: 'type', target: 'article-brand', text: '{{tourRef}}' },
    ]),
    master('article-packaging', '/articles', [
        { kind: 'select', target: 'article-packaging-unit', value: 'colli' },
        { kind: 'type', target: 'article-packaging', text: '4 kg doos' },
        { kind: 'select', target: 'article-weight-method', value: 'Calculated' },
        { kind: 'type', target: 'article-net-kg', text: '4' },
        { kind: 'type', target: 'article-cpp', text: '240' },
    ]),
    master('article-save', '/articles', [
        { kind: 'click', target: 'article-save' },
        { kind: 'wait', target: 'article-notice' },
        { kind: 'highlight', target: 'article-notice' },
    ]),
];

export const MASTERDATA_CHAPTER: TourChapter = {
    id: 'masterdata',
    titleKey: 'tour_ch_master_title',
    summaryKey: 'tour_ch_master_summary',
    doneKey: 'tour_ch_master_done',
    writes: true,
    precheck: precheckMasterdata,
    steps: [...VENDOR_STEPS, ...CUSTOMER_STEPS, ...ARTICLE_STEPS],
};
