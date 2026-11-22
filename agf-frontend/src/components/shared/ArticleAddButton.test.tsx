import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import type { ProductOption } from '../../types/referenceData';
import ArticleAddButton from './ArticleAddButton';

// Feedback 22 november: "Artikel toevoegen" is een lijst van bestaande artikelen, geen vrije keuze. Enter of klik kiest direct.
const variant = (id: number, name: string, extra: Partial<ProductOption['variants'][number]> = {}) => ({
    id, name, origin: 'Peru', originCountryCode: 'PE', packaging: '4kg Box', sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null, ...extra,
});
const products: ProductOption[] = [
    { id: 1, name: 'Avocado', variants: [variant(10, 'Hass', { shortcutCode: 'avohass', sizeCategory: '18' }), variant(11, 'Green', { origin: 'Spanje', originCountryCode: 'ES' }), variant(12, 'Oud', { isArchived: true })] },
    { id: 2, name: 'Gember', variants: [variant(20, '', { origin: 'China', originCountryCode: 'CN', packaging: null })] },
];

let mounted: Mounted;
let picked: number[];
let created: number;
const open = async () => { await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour="supply-article-add"]')!.click(); }); };
const search = () => mounted.container.querySelector<HTMLInputElement>('[data-tour="article-add-search"]')!;
const key = async (name: string) => { await act(async () => { search().dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); }); };
const options = () => Array.from(mounted.container.querySelectorAll('[role="option"]')).map((option) => option.textContent ?? '');

describe('ArticleAddButton', () => {
    beforeEach(async () => {
        picked = [];
        created = 0;
        mounted = new Mounted();
        await mounted.render(<ArticleAddButton products={products} dataTour="supply-article-add" onPick={(id) => picked.push(id)} onNewArticle={() => { created += 1; }} />);
    });
    afterEach(async () => { await mounted.unmount(); });

    test('opens a list of all active articles with the search field focused; archived articles are not offered', async () => {
        await open();
        assert.equal(window.document.activeElement, search());
        assert.deepEqual(options(), ['Avocado · Green · 4kg Box · Spanje', 'avohassAvocado · Hass · 4kg Box · Peru · 18', 'Gember · China']);
    });

    test('searches by code, name, origin (also the country code) and size; Enter puts the first match at once', async () => {
        await open();
        await act(async () => { setValue(search(), 'cn'); });
        assert.deepEqual(options(), ['Gember · China']);
        await act(async () => { setValue(search(), '18'); });
        assert.deepEqual(options(), ['avohassAvocado · Hass · 4kg Box · Peru · 18']);
        await act(async () => { setValue(search(), 'avohass'); });
        await key('Enter');
        assert.deepEqual(picked, [10]);
        assert.equal(mounted.container.querySelector('[data-testid="article-add-list"]'), null, 'the list closes');
    });

    test('arrow keys move the choice; Escape closes without choosing and the button gets the focus back', async () => {
        await open();
        await act(async () => { setValue(search(), 'avocado'); });
        await key('ArrowDown');
        await key('Enter');
        assert.deepEqual(picked, [10], 'second avocado = Hass');
        await open();
        await key('Escape');
        assert.equal(mounted.container.querySelector('[data-testid="article-add-list"]'), null);
        assert.deepEqual(picked, [10]);
        await new Promise((resolve) => setTimeout(resolve, 5));
        assert.equal(window.document.activeElement, mounted.container.querySelector('[data-tour="supply-article-add"]'));
    });

    test('review #313: reopening an unfiltered list starts at the first article again', async () => {
        await open();
        await key('ArrowDown');
        await key('Escape');
        await open();
        await key('Enter');
        assert.deepEqual(picked, [11], 'the first article (Green), not the one highlighted before');
    });

    test('a click chooses as well; "New article" asks for the side panel', async () => {
        await open();
        await act(async () => { (mounted.container.querySelectorAll<HTMLElement>('[role="option"]')[2]).click(); });
        assert.deepEqual(picked, [20]);
        await open();
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour="article-add-new"]')!.click(); });
        assert.equal(created, 1);
    });
});
