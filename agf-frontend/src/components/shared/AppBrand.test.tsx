import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { APP_NAME } from '../../lib/appName.ts';
import Home from '../../app/page';
import AppBrand from './AppBrand';

// De naam VerseFlow links boven als link naar Start, en wie de software opent (/) komt direct op Start.
describe('app name and start', () => {
    let mounted: Mounted;
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('the header shows VerseFlow as a link to Start', async () => {
        assert.equal(APP_NAME, 'VerseFlow');
        await mounted.render(<AppBrand />);
        const link = mounted.container.querySelector<HTMLAnchorElement>('[data-testid="app-brand"]')!;
        assert.equal(link.textContent, 'VerseFlow');
        assert.equal(link.getAttribute('href'), '/dashboard');
    });

    test('opening the software at / goes straight to Start, without the old choice screen', () => {
        assert.throws(() => Home(), (error: unknown) => String((error as { digest?: string }).digest).includes(';/dashboard;'));
    });
});
