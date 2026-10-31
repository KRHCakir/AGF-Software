import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import type { Article, ArticleUsage } from '../../types/article';
import ArticleLifecycle from './ArticleLifecycle';

// Artikel beheren: verwijderen is uitgeschakeld met de reden als er iets aan hangt; anders eerst een bevestiging met de volledige
// naam (leeg = "geen", Annuleren heeft de focus, Escape annuleert). Archiveren en terugzetten gaan direct, met de medewerker.

const article: Article = {
    id: 7, productId: 1, productName: 'Avocado', variantName: 'Hass', description: 'Avocado · Hass · Zuid-Afrika · 12', shortcutCode: null, origin: 'Zuid-Afrika',
    originCountryCode: 'ZA', brand: null, sizeCategory: '12', packaging: 'test', packagingUnit: 'colli', season: null, notes: null, weightMethod: 'Calculated',
    defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'I', defaultPrice: 0,
    isArchived: false, archivedAt: null, archivedBy: null,
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let usage: ArticleUsage;
let answer: () => Promise<Response>;

const doc = () => window.document;
const button = (text: string) => Array.from(doc().querySelectorAll<HTMLButtonElement>('button')).find((candidate) => candidate.textContent === text)!;
const dialog = () => doc().querySelector<HTMLElement>('[data-testid="article-delete-dialog"]');

describe('ArticleLifecycle', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Jan');
        answer = async () => json(500, {});
        posted = stubFetch({ onGet: () => json(200, usage), nextPost: () => answer() });
        mounted = new Mounted();
    });
    afterEach(async () => {
        await mounted.unmount();
        window.localStorage.removeItem('agf_actor_reference');
    });

    test('an article in use cannot be deleted: the button is off and says why; archiving is offered and sends who did it', async () => {
        usage = { articleId: 7, canDelete: false, usages: [{ kind: 'receipts', count: 3 }], message: 'Dit artikel is gebruikt in 3 ontvangsten' };
        let changed: Article | null = null;
        await mounted.render(<ArticleLifecycle article={article} onChanged={(saved) => { changed = saved; }} onDeleted={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="article-delete-reason"]') !== null, 'usage loaded');
        const remove = button('articles_delete');
        assert.equal(remove.disabled, true);
        assert.equal(mounted.container.querySelector('[data-testid="article-delete-reason"]')!.textContent, 'articles_delete_blocked');
        assert.equal(remove.getAttribute('aria-describedby'), mounted.container.querySelector('[data-testid="article-delete-reason"]')!.id);

        answer = async () => json(200, { ...article, isArchived: true, archivedAt: '2026-10-31T10:00:00Z', archivedBy: 'Jan' });
        await click(button('articles_archive'));
        await mounted.waitFor(() => changed !== null, 'archived');
        assert.equal(posted[0].url.endsWith('/articles/7/archive'), true);
        assert.deepEqual(JSON.parse(posted[0].body), { actorReference: 'Jan' });
    });

    test('an unused article asks first with the full name; Cancel has the focus and Escape cancels; Yes deletes', async () => {
        usage = { articleId: 7, canDelete: true, usages: [], message: null };
        let deleted: number | null = null;
        await mounted.render(<ArticleLifecycle article={article} onChanged={() => {}} onDeleted={(id) => { deleted = id; }} />);
        await mounted.waitFor(() => !button('articles_delete').disabled, 'usage loaded');

        await click(button('articles_delete'));
        assert.ok(dialog(), 'confirmation first, nothing sent');
        assert.equal(posted.length, 0);
        const rows = Array.from(dialog()!.querySelectorAll('dl > div')).map((row) => Array.from(row.children).map((cell) => cell.textContent));
        assert.deepEqual(rows, [
            ['articles_name_product', 'Avocado Hass'], ['articles_name_origin', 'Zuid-Afrika'], ['articles_name_size', '12'],
            ['articles_name_packaging', 'test'], ['articles_name_trade_class', 'I'], ['articles_name_brand', 'articles_value_none'],
        ]);
        assert.equal(doc().activeElement?.textContent, 'articles_delete_cancel');
        await act(async () => { dialog()!.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); });
        assert.equal(dialog(), null);
        assert.equal(posted.length, 0);

        answer = async () => new Response(null, { status: 204 });
        await click(button('articles_delete'));
        await click(button('articles_delete_confirm'));
        await mounted.waitFor(() => deleted === 7, 'deleted');
        assert.equal(posted[0].url.endsWith('/articles/7/delete'), true);
    });

    test('used in the meantime: the server reason is shown and the button goes off', async () => {
        usage = { articleId: 7, canDelete: true, usages: [], message: null };
        await mounted.render(<ArticleLifecycle article={article} onChanged={() => {}} onDeleted={() => { throw new Error('not deleted'); }} />);
        await mounted.waitFor(() => !button('articles_delete').disabled, 'usage loaded');
        answer = async () => {
            usage = { articleId: 7, canDelete: false, usages: [{ kind: 'purchaseOrderLines', count: 1 }], message: 'x' };
            return json(409, { message: 'Dit artikel is intussen in gebruik genomen', code: 'article-in-use' });
        };
        await click(button('articles_delete'));
        await click(button('articles_delete_confirm'));
        await mounted.waitFor(() => mounted.hasText('Dit artikel is intussen in gebruik genomen'), 'server reason');
        await mounted.waitFor(() => button('articles_delete').disabled, 'usage reloaded');
        assert.equal(dialog(), null);
    });
});
