import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';
import { articleKinds } from './NewSupplyArticle';

// Aanvoer plannen met een artikel dat nog niet bestaat: artikel (product en ras) kiezen, herkomst en wat er verder op de doos staat
// typen, "Nieuw artikel: …" met "Bedoel je dit?" en één knop; het aangemaakte (of gekozen) artikel komt op de regel en gaat mee.

const hass = (id: number, origin: string, code: string, size: string, extra: Record<string, unknown> = {}) => ({
    id, name: 'Hass', origin, originCountryCode: code, packaging: '4 kg doos', sizeCategory: size, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli' as const,
    tradeClass: 'I', brand: null, defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: null, ...extra,
});
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [
        { id: 1, name: 'Avocado', variants: [hass(10, 'Peru', 'PE', '18'), hass(12, 'Zuid-Afrika', 'ZA', '18'), { ...hass(13, 'Peru', 'PE', '8'), name: 'Fuerte' }] },
        { id: 2, name: 'Mango', variants: [{ ...hass(20, 'Brazilië', 'BR', '8'), name: 'Palmer' }] },
    ],
};
const countries = { countries: [{ code: 'PE', nameNl: 'Peru', nameEn: 'Peru' }, { code: 'ZA', nameNl: 'Zuid-Afrika', nameEn: 'South Africa' }], aliases: [] };
const created = {
    id: 50, productId: 1, productName: 'Avocado', variantName: 'Hass', description: 'Avocado · Hass · test · Zuid-Afrika · 12', shortcutCode: null, origin: 'Zuid-Afrika',
    originCountryCode: 'ZA', brand: 'test', sizeCategory: '12', packaging: 'test', packagingUnit: 'colli', season: null, notes: null, weightMethod: 'Calculated',
    defaultNetKgPerPackage: 4.5, defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'I', defaultPrice: 0, isArchived: false,
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let answers: (() => Promise<Response>)[];

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const tour = <T extends HTMLElement>(name: string) => mounted.container.querySelector<T>(`[data-tour="${name}"]`)!;
const type = (element: HTMLInputElement, text: string) => act(async () => { element.focus(); setValue(element, text); });
const enter = (element: HTMLElement) => act(async () => { element.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

async function openNewArticle() {
    await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
    await click(tour('supply-line-new-article'));
    await act(async () => { setSelectValue(tour<HTMLSelectElement>('supply-new-article-kind'), '1|hass'); });
    await mounted.waitFor(() => mounted.container.querySelector('[data-tour="supply-new-article-origin"]') !== null, 'countries loaded');
    await type(tour<HTMLInputElement>('supply-new-article-origin'), 'ZA');
    await enter(tour('supply-new-article-origin'));
    await type(tour<HTMLInputElement>('supply-new-article-size'), '12');
    await type(tour<HTMLInputElement>('supply-new-article-packaging'), 'test');
    await type(tour<HTMLInputElement>('supply-new-article-tradeClass'), 'I');
}

describe('PlanSupplyForm: new article on a supply line', () => {
    beforeEach(() => {
        answers = [];
        posted = stubFetch({
            onGet: (url) => (url.includes('/countries') ? json(200, countries) : json(404, {})),
            nextPost: () => answers.shift()?.() ?? Promise.resolve(json(500, { message: 'stop' })),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('only existing articles (product and variety) can be chosen; a new product or variety stays in Articles', () => {
        assert.deepEqual(articleKinds(reference.products).map((kind) => [kind.label, kind.template.id]), [['Avocado Fuerte', 13], ['Avocado Hass', 10], ['Mango Palmer', 20]]);
    });

    test('shows exactly what is created and what looks like it; one click creates it and puts it on the line, which is then sent', async () => {
        await openNewArticle();
        assert.equal(mounted.container.querySelector('[data-testid="new-article-text"]')?.textContent, 'Avocado Hass · Zuid-Afrika · new_article_size 12 · new_article_packaging test · new_article_trade_class I · new_article_brand articles_value_none');
        // Zuid-Afrika 18 verschilt alleen in maat (en verpakking: "test" tegen "4 kg doos") — twee verschillen, dus geen suggestie.
        assert.equal(mounted.container.querySelectorAll('[data-testid="new-article-similar"]').length, 0);
        await type(tour<HTMLInputElement>('supply-new-article-brand'), 'test');
        await type(tour<HTMLInputElement>('supply-new-article-kg'), '4,5');

        answers.push(async () => json(201, { article: created, replayed: false }));
        await click(tour('new-article-create'));
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="supply-new-article"]') === null, 'block closed');
        const body = JSON.parse(posted[0].body);
        assert.deepEqual({ ...body, requestId: typeof body.requestId }, {
            requestId: 'string', source: 'Supply', templateVariantId: 10, originCountryCode: 'ZA', sizeCategory: '12', packaging: 'test', tradeClass: 'I', brand: 'test',
            netKgPerPackage: 4.5, actorReference: null,
        });
        assert.ok(field('lines[0].productVariantId-input').value.includes('Zuid-Afrika'), 'the new article is on the line');

        await type(field('vendorId-input'), 'supb');
        await enter(field('vendorId-input'));
        await type(field('expectedAt-input'), '2026-11-02');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
        await type(field('lines[0].expectedQuantity-input'), '10');
        await submit();
        await mounted.waitFor(() => posted.length === 2, 'supply POST');
        assert.equal(JSON.parse(posted[1].body).lines[0].productVariantId, 50);
    });

    test('while the article is being created, every field of the block is locked', async () => {
        await openNewArticle();
        let release: () => void = () => {};
        answers.push(() => new Promise<Response>((resolve) => { release = () => resolve(json(201, { article: created, replayed: false })); }));
        await click(tour('new-article-create'));
        for (const name of ['supply-new-article-kind', 'supply-new-article-origin', 'supply-new-article-size', 'supply-new-article-brand', 'supply-new-article-kg']) {
            assert.equal(tour<HTMLInputElement>(name).disabled, true, name);
        }
        await act(async () => { release(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="supply-new-article"]') === null, 'block closed');
    });

    test('"Choose this" puts the existing variant on the line and creates nothing', async () => {
        await openNewArticle();
        await type(tour<HTMLInputElement>('supply-new-article-packaging'), '4 kg doos');
        const choose = Array.from(mounted.container.querySelectorAll<HTMLButtonElement>('[data-tour="new-article-choose"]'));
        assert.equal(choose.length, 1, 'Zuid-Afrika 18 differs only in size');
        await click(choose[0]);
        assert.equal(posted.length, 0);
        assert.ok(field('lines[0].productVariantId-input').value.includes('18'));
        assert.equal(mounted.container.querySelector('[data-tour="supply-new-article"]'), null);
    });
});
