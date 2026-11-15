import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, json } from '../../test/reactHarness.ts';
import DemoEnvironmentBar from './DemoEnvironmentBar';

// De demo-balk: alleen in de demo-omgeving, op elk scherm (hoofdlayout), zonder sluitknop; achter Cloudflare Access met het e-mailadres.
let mounted: Mounted;
let urls: string[];
let me: Record<string, unknown>;
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

describe('demo environment bar', () => {
    beforeEach(() => {
        urls = [];
        me = { userId: 'admin', displayName: 'Admin', roles: ['Admin'], permissions: [], accessUser: null };
        globalThis.fetch = (async (input: RequestInfo | URL) => { urls.push(String(input)); return json(200, me); }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('outside the demo environment there is no bar and nothing is asked', async () => {
        await mounted.render(<DemoEnvironmentBar enabled={false} />);
        await settle();
        assert.equal($('[data-testid="demo-environment-bar"]'), null);
        assert.deepEqual(urls, []);
    });

    test('in the demo environment the bar says to use made-up data only, cannot be closed, and shows the Access e-mail address', async () => {
        await mounted.render(<DemoEnvironmentBar enabled />);
        await settle();
        const bar = $('[data-testid="demo-environment-bar"]')!;
        assert.equal(bar.textContent, 'Demo-omgeving: gebruik alleen verzonnen gegevens. Gegevens kunnen worden teruggezet.');
        assert.equal(bar.querySelector('button'), null, 'no close button');
        assert.equal($('[data-testid="demo-access-user"]'), null, 'without Access no address');
        assert.ok(urls.some((url) => url.endsWith('/me')));

        await mounted.unmount();
        me = { ...me, accessUser: 'klant@voorbeeld.nl' };
        mounted = new Mounted();
        await mounted.render(<DemoEnvironmentBar enabled />);
        await settle();
        assert.equal($('[data-testid="demo-access-user"]')!.textContent, 'Toegang: klant@voorbeeld.nl');
    });
});
