import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import CertificateExpiryList from './CertificateExpiryList';

// "Certificaten die bijna verlopen": op volgorde van de server (einddatum), met wat te doen per soort relatie en een link naar de relatie;
// op de startpagina niets als er niets is.
let mounted: Mounted;
let items: unknown[];
const item = (over: Record<string, unknown>) => ({
    certificateId: 1, relationId: 4, relationName: 'Teler Uno', isCustomer: false, isVendor: true, isCarrier: false, typeCode: 'GlobalGapIfa', typeName: 'GLOBALG.A.P. IFA (GGN)',
    number: '4049928415684', validUntil: '2026-11-18', daysLeft: -2, status: 'Expired', productsPending: false, isActive: true, productNames: ['Mango'], ...over,
});

describe('CertificateExpiryList', () => {
    beforeEach(() => {
        items = [];
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            assert.ok(String(input).includes('/certificates?status=attention'));
            return json(200, { warningDays: 30, total: items.length, items });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('on the start page nothing shows when nothing is about to expire', async () => {
        await mounted.render(<CertificateExpiryList compact />);
        await new Promise((resolve) => setTimeout(resolve, 10));
        assert.equal(mounted.container.querySelector('[data-testid="certificate-expiry"]'), null);
    });

    test('a supplier: check for a new certificate; a customer: request it; own company: renew; each with a link', async () => {
        items = [item({}), item({ certificateId: 2, relationId: 5, relationName: 'Klant Groot', isCustomer: true, isVendor: false, typeCode: 'Brcgs', typeName: 'BRCGS', status: 'ExpiringSoon', daysLeft: 12 }),
            item({ certificateId: 3, relationId: null, relationName: null, isVendor: false, typeCode: 'Skal', typeName: 'SKAL', status: 'ExpiringSoon', daysLeft: 30 })];
        await mounted.render(<CertificateExpiryList compact />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="certificate-expiry-3"]'), 'list');
        const row = (id: number) => mounted.container.querySelector(`[data-testid="certificate-expiry-${id}"]`)!;
        assert.ok(row(1).textContent!.includes('cert_action_vendor'));
        assert.ok(row(2).textContent!.includes('cert_action_request'));
        assert.ok(row(3).textContent!.includes('cert_action_own'));
        assert.equal(row(1).querySelector('a')!.getAttribute('href'), '/relations?open=4&section=certificates');
        assert.equal(row(3).querySelector('a')!.getAttribute('href'), '/settings#certificaten');
        assert.deepEqual(Array.from(mounted.container.querySelectorAll('[data-status]')).map((e) => e.getAttribute('data-status')), ['Expired', 'ExpiringSoon', 'ExpiringSoon']);
    });
});
