import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { currentUrl, navigate } from '../../test/nextNavigationStub.ts';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import type { Article, ArticleReference } from '../../types/article';
import ArticleForm from '../articles/ArticleForm';
import LeaveGuardProvider, { guardStateOf, leavingHref, useFormLeaveGuard } from './LeaveGuard';

// Fixes 5b, een fout blokkeert het weggaan: met een invoerfout of niet-opgeslagen wijzigingen vraagt elke link naar een andere pagina
// (menu, keten-strip), de browserknop Terug en het sluiten van het tabblad eerst; een onaangeroerd formulier blokkeert niets, en
// Annuleren en weggooien werkt ook zonder fout.

function Form({ error = false, onSaved }: { error?: boolean; onSaved?: (saved: () => void) => void }) {
    const [value, setValueState] = useState('');
    const leave = useFormLeaveGuard(error);
    onSaved?.(leave.saved);
    return (
        <form ref={leave.ref}>
            <input id="field" value={value} onChange={(event) => setValueState(event.target.value)} />
            {error && <p>fout</p>}
        </form>
    );
}

let mounted: Mounted;
let reached: string[];
const arrive = (event: MouseEvent) => {
    const anchor = (event.target as Element).closest('a');
    if (anchor) { reached.push(anchor.getAttribute('href')!); event.preventDefault(); }
};
const link = (href: string) => mounted.container.querySelector<HTMLAnchorElement>(`a[href="${href}"]`)!;
const follow = async (href: string) => {
    await act(async () => { link(href).dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })); });
};
const dialog = () => mounted.container.querySelector('[data-testid="leave-guard"]');
const type = async (text: string) => { await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#field')!, text); }); };

function page(form: React.ReactNode) {
    return (
        <LeaveGuardProvider>
            <nav><a href="/availability">Beschikbaarheid</a><a href="/purchase-orders?id=4">IO-2026-0004</a></nav>
            {form}
        </LeaveGuardProvider>
    );
}

describe('LeaveGuard', () => {
    beforeEach(() => {
        navigate('/relations');
        window.history.replaceState(null, '', '/relations');
        reached = [];
        mounted = new Mounted();
        // Een link die niet tegengehouden wordt, komt bij het element aan; jsdom navigeert zelf niet.
        window.document.addEventListener('click', arrive);
    });
    afterEach(async () => { await mounted.unmount(); window.document.removeEventListener('click', arrive); });

    test('an untouched form blocks nothing', async () => {
        await mounted.render(page(<Form />));
        await follow('/availability');
        assert.deepEqual(reached, ['/availability']);
        assert.equal(dialog(), null);
    });

    test('unsaved changes: the menu link asks first; Back to the form keeps the input; Cancel and discard leaves (also without an error)', async () => {
        await mounted.render(page(<Form />));
        await type('Kwekerij');
        await follow('/availability');
        assert.deepEqual(reached, [], 'the link did not go through');
        assert.ok(dialog()?.textContent?.includes('leave_guard_unsaved'));
        assert.equal(window.document.activeElement, mounted.container.querySelector('[data-testid="leave-guard-stay"]'), 'Back to the form is the default');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="leave-guard-stay"]')!.click(); });
        assert.equal(dialog(), null);
        assert.equal(mounted.container.querySelector<HTMLInputElement>('#field')!.value, 'Kwekerij', 'input kept');
        await follow('/purchase-orders?id=4');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="leave-guard-discard"]')!.click(); });
        assert.equal(currentUrl(), '/purchase-orders?id=4', 'went to the chain-strip link');
    });

    test('an input error says so in the question', async () => {
        await mounted.render(page(<Form error />));
        await follow('/availability');
        assert.ok(dialog()?.textContent?.includes('leave_guard_error'));
    });

    test('closing the tab or reloading gets the browser warning; after saving nothing is blocked any more', async () => {
        let saved: () => void = () => {};
        await mounted.render(page(<Form onSaved={(fn) => { saved = fn; }} />));
        const unload = () => { const event = new window.Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; };
        assert.equal(unload(), false, 'untouched');
        await type('x');
        assert.equal(unload(), true, 'changed');
        await act(async () => { saved(); });
        assert.equal(unload(), false, 'saved');
        await follow('/availability');
        assert.deepEqual(reached, ['/availability']);
    });

    test('Back or Forward (also several steps through the history menu) asks the same question before Next.js shows the other page', async () => {
        let routerSaw = 0;
        // Zoals de luisteraar van Next.js: eerder geregistreerd, zonder capture.
        const next = () => { routerSaw += 1; };
        window.addEventListener('popstate', next);
        try {
            await mounted.render(page(<Form />));
            await type('x');
            // De browser staat al op een andere regel (bijvoorbeeld twee stappen terug via het geschiedenismenu).
            window.history.pushState(null, '', '/purchase-orders?id=4');
            await act(async () => { window.dispatchEvent(new window.PopStateEvent('popstate', { state: null })); });
            assert.ok(dialog()?.textContent?.includes('leave_guard_unsaved'));
            assert.equal(routerSaw, 0, 'Next.js never saw the navigation');
            assert.equal(window.location.pathname + window.location.search, '/relations', 'the form page is back in the address bar');
            await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="leave-guard-discard"]')!.click(); });
            assert.equal(currentUrl(), '/purchase-orders?id=4', 'discard goes where the user wanted');
        } finally {
            window.removeEventListener('popstate', next);
        }
    });

    test('an anchor on the same page (#section) is not leaving', async () => {
        await mounted.render(page(<Form />));
        await type('x');
        window.history.pushState(null, '', '/relations#adressen');
        await act(async () => { window.dispatchEvent(new window.PopStateEvent('popstate', { state: null })); });
        assert.equal(dialog(), null);
    });

    test('a real create form (Artikel) is guarded as soon as the user types', async () => {
        const reference: ArticleReference = { productNames: ['Avocado'], packagingUnits: ['colli'], weightMethods: ['Weighed', 'Calculated'], goodsCodeSystems: ['HS'], tradeClassSuggestions: ['I'] };
        await mounted.render(page(<ArticleForm article={null as Article | null} reference={reference} countries={{ countries: [], aliases: [] }} onSaved={() => {}} onClose={() => {}} />));
        await follow('/availability');
        assert.deepEqual(reached, ['/availability'], 'just opened: not blocked');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="productName-input"]')!, 'Mango'); });
        await follow('/availability');
        assert.deepEqual(reached, ['/availability'], 'second click did not go through');
        assert.ok(dialog());
    });

    test('rules: error beats changes; only links to another page of this website count', () => {
        assert.equal(guardStateOf([]), 'none');
        assert.equal(guardStateOf([{ dirty: true, error: false }, { dirty: false, error: false }]), 'dirty');
        assert.equal(guardStateOf([{ dirty: true, error: false }, { dirty: false, error: true }]), 'error');
        const at = { href: 'http://localhost/relations?x=1', origin: 'http://localhost', pathname: '/relations', search: '?x=1' };
        const anchor = (href: string, extra: Record<string, string> = {}) => {
            const element = window.document.createElement('a');
            element.setAttribute('href', href);
            for (const [name, value] of Object.entries(extra)) element.setAttribute(name, value);
            return element;
        };
        const click = (element: Element, over: Partial<MouseEvent> = {}) => ({ button: 0, metaKey: false, ctrlKey: false, shiftKey: false, altKey: false, defaultPrevented: false, target: element, ...over }) as MouseEvent;
        assert.equal(leavingHref(click(anchor('/availability')), at), '/availability');
        assert.equal(leavingHref(click(anchor('/relations?x=2')), at), '/relations?x=2', 'same page, other selection');
        assert.equal(leavingHref(click(anchor('/relations?x=1#top')), at), null, 'same page');
        assert.equal(leavingHref(click(anchor('https://example.org/')), at), null, 'other website');
        assert.equal(leavingHref(click(anchor('/availability', { target: '_blank' })), at), null, 'new tab');
        assert.equal(leavingHref(click(anchor('/availability'), { metaKey: true }), at), null, 'Cmd-click opens a new tab');
    });
});
