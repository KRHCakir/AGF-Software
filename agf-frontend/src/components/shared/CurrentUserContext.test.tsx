import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import { isCurrentUser } from '../../types/currentUser.ts';
import { CurrentUserProvider, SignedInAs } from './CurrentUserContext';

// De ingelogde gebruiker komt van GET /api/me; schermen tonen alleen "Ingelogd als …" en vragen nergens een naam.

let mounted: Mounted;
let requested: string[];

describe('CurrentUserContext', () => {
    beforeEach(() => {
        mounted = new Mounted();
        requested = [];
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the name of the signed-in user, read once from /api/me, without any input', async () => {
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            requested.push(String(input));
            return json(200, { userId: 'admin', displayName: 'Jan', roles: ['Admin'], permissions: ['Verkoop'] });
        }) as typeof fetch;
        await mounted.render(<CurrentUserProvider><SignedInAs /><SignedInAs /></CurrentUserProvider>);
        const shown = await mounted.waitFor(() => mounted.container.querySelector('[data-testid="signed-in-as"]'), 'signed in as');
        assert.equal(shown.textContent, 'signed_in_as');
        assert.equal(requested.filter((url) => url.endsWith('/me')).length, 1);
        assert.equal(mounted.container.querySelectorAll('input').length, 0);
    });

    test('without an answer from the API nothing is shown (the server records the user itself)', async () => {
        globalThis.fetch = (async () => json(500, {})) as typeof fetch;
        await mounted.render(<CurrentUserProvider><SignedInAs /></CurrentUserProvider>);
        await new Promise((resolve) => setTimeout(resolve, 20));
        assert.equal(mounted.container.querySelector('[data-testid="signed-in-as"]'), null);
    });

    test('the shape check refuses an answer without a name or with non-text roles', () => {
        assert.equal(isCurrentUser({ userId: 'a', displayName: 'Jan', roles: [], permissions: [] }), true);
        assert.equal(isCurrentUser({ userId: 'a', roles: [], permissions: [] }), false);
        assert.equal(isCurrentUser({ userId: 'a', displayName: 'Jan', roles: [1], permissions: [] }), false);
        assert.equal(isCurrentUser(null), false);
    });
});
