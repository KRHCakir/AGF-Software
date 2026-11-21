import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import CertificatesSetting from './CertificatesSetting';

// Instellingen › Certificaten: de dagen vooraf en de eigen soorten opslaan, en de certificaten van het eigen bedrijf (api/certificates/own)
// met hetzelfde blok als bij een relatie; een bestand bij een nieuw certificaat gaat er direct na het opslaan bij.
let mounted: Mounted;
let calls: { method: string; url: string; body: unknown }[];
const $ = <T extends HTMLElement = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
const REFERENCE = {
    types: [{ code: 'Brcgs', name: 'BRCGS', description: '', typicalHolder: '', productsRequired: false, ggnNumber: false }],
    customTypes: [], warningDays: 30, globalGapPortalUrl: 'https://portal.example', globalGapDatabaseUrl: 'https://database.example', maxFileBytes: 1000,
};
const ownCertificate = (files: unknown[] = []) => ({
    id: 3, typeCode: 'Brcgs', customTypeName: null, typeName: 'BRCGS', number: 'BRC-OWN', certificationBody: null, validFrom: null, validUntil: '2026-12-01',
    products: [], productsPending: false, producerName: null, hasGgnLabel: false, checkedOn: null, checkedBy: null, isActive: true, status: 'ExpiringSoon', daysLeft: 11, files,
});

describe('certificates setting', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            const method = init?.method ?? 'GET';
            calls.push({ method, url, body: typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body ?? null });
            if (url.endsWith('/certificates/settings')) return json(200, method === 'PUT' ? { warningDays: 45, customTypes: ['Demeter'] } : { warningDays: 30, customTypes: [] });
            if (url.endsWith('/certificates/reference')) return json(200, REFERENCE);
            if (url.endsWith('/products')) return json(200, []);
            if (url.endsWith('/certificates/own')) return json(200, method === 'PUT'
                ? { version: 'v1', warningDays: 30, warnings: [], certificates: [ownCertificate()] }
                : { version: '0', warningDays: 30, warnings: [], certificates: [] });
            if (url.endsWith('/certificates/3/files')) return json(201, { id: 8, version: 1, fileName: 'brc.pdf', mediaType: 'application/pdf', sizeBytes: 8, sha256: 'a', createdAt: '2026-11-20T09:00:00Z', createdBy: 'Anja' });
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('warning days and own types are saved; an own certificate is saved with its file right after', async () => {
        await mounted.render(<CertificatesSetting />);
        await settle();
        assert.equal($<HTMLInputElement>('#certificate-warning-days')!.value, '30');
        await act(async () => { setValue($<HTMLInputElement>('#certificate-warning-days')!, '45'); });
        await act(async () => { setValue($<HTMLTextAreaElement>('#certificate-custom-types')!, 'Demeter\n\n'); });
        await act(async () => { $<HTMLButtonElement>('[data-testid="certificate-settings-save"]')!.click(); });
        await settle();
        assert.deepEqual(calls.find((c) => c.method === 'PUT' && c.url.endsWith('/certificates/settings'))!.body, { warningDays: 45, customTypes: ['Demeter'] });
        assert.ok(mounted.hasText('cert_settings_saved'));

        assert.equal($<HTMLButtonElement>('[data-testid="own-certificates-save"]')!.disabled, true, 'nothing to save yet');
        await act(async () => { mounted.button('+ cert_add')!.click(); });
        const row = $('[data-testid="certificate-0"]')!.getAttribute('data-row')!;
        await act(async () => { setSelectValue($<HTMLSelectElement>(`[id="certificates.${row}.typeCode-input"]`)!, 'Brcgs'); });
        await act(async () => { setValue($<HTMLInputElement>(`[id="certificates.${row}.number-input"]`)!, 'BRC-OWN'); });
        const input = $<HTMLInputElement>('[data-testid="certificate-0-file"]')!;
        Object.defineProperty(input, 'files', { value: [new globalThis.File(['%PDF-1.4'], 'brc.pdf', { type: 'application/pdf' })], configurable: true });
        await act(async () => { input.dispatchEvent(new window.Event('change', { bubbles: true })); });
        await act(async () => { $<HTMLButtonElement>('[data-testid="own-certificates-save"]')!.click(); });
        await mounted.waitFor(() => mounted.hasText('cert_own_saved'), 'saved');
        const put = calls.find((c) => c.method === 'PUT' && c.url.endsWith('/certificates/own'))!;
        assert.deepEqual((put.body as { expectedVersion: string; certificates: { typeCode: string; number: string }[] }).certificates.map((c) => [c.typeCode, c.number]), [['Brcgs', 'BRC-OWN']]);
        assert.ok(calls.some((c) => c.method === 'POST' && c.url.endsWith('/certificates/3/files')), 'the file goes to the new certificate');
        assert.ok(mounted.hasText('cert_own_saved'));
        assert.ok(mounted.hasText('brc.pdf'));
        assert.equal($('[data-testid="certificate-0-status"]')!.getAttribute('data-status'), 'ExpiringSoon');
    });

    test('Open shows the file in a window opened on the click; a blocked window downloads it instead', async () => {
        const file = { id: 8, version: 1, fileName: 'brc.pdf', mediaType: 'application/pdf', sizeBytes: 8, sha256: 'a', createdAt: '2026-11-20T09:00:00Z', createdBy: 'Anja' };
        const base = globalThis.fetch;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            if (url.endsWith('/certificates/own')) return json(200, { version: 'v1', warningDays: 30, warnings: [], certificates: [ownCertificate([file])] });
            if (url.endsWith('/certificates/3/files/8')) return new Response(new Blob(['%PDF-1.4']), { status: 200, headers: { 'Content-Type': 'application/pdf' } });
            return base(input, init);
        }) as typeof fetch;
        const originalOpen = window.open;
        const originalCreate = URL.createObjectURL;
        URL.createObjectURL = (() => 'blob:brc') as typeof URL.createObjectURL;
        const opened = { opener: {} as unknown, closed: false, location: { href: '' }, close() { this.closed = true; } };
        try {
            window.open = (() => opened) as unknown as typeof window.open;
            await mounted.render(<CertificatesSetting />);
            await mounted.waitFor(() => mounted.button('cert_file_open') !== null, 'file listed');
            await act(async () => { mounted.button('cert_file_open')!.click(); });
            await mounted.waitFor(() => opened.location.href === 'blob:brc', 'loaded in the window');
            assert.equal(opened.opener, null, 'the new window gets no access back');

            let downloaded = '';
            const originalClick = window.HTMLAnchorElement.prototype.click;
            window.HTMLAnchorElement.prototype.click = function click(this: HTMLAnchorElement) { downloaded = this.download; };
            try {
                window.open = (() => null) as typeof window.open;
                await act(async () => { mounted.button('cert_file_open')!.click(); });
                await mounted.waitFor(() => downloaded === 'brc.pdf', 'downloaded');
            } finally {
                window.HTMLAnchorElement.prototype.click = originalClick;
            }
        } finally {
            window.open = originalOpen;
            URL.createObjectURL = originalCreate;
        }
    });

    test('a new own type ends up in the settings field even when the file upload after saving fails', async () => {
        const base = globalThis.fetch;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            const method = init?.method ?? 'GET';
            if (url.endsWith('/certificates/settings') && method === 'GET') return json(200, { warningDays: 30, customTypes: calls.some((c) => c.method === 'PUT' && c.url.endsWith('/certificates/own')) ? ['Demeter'] : [] });
            if (url.endsWith('/certificates/own') && method === 'PUT') {
                calls.push({ method, url, body: null });
                return json(200, { version: 'v1', warningDays: 30, warnings: [], certificates: [{ ...ownCertificate(), typeCode: 'Custom', customTypeName: 'Demeter', typeName: 'Demeter' }] });
            }
            if (url.endsWith('/certificates/3/files')) return json(500, { message: 'disk full' });
            return base(input, init);
        }) as typeof fetch;
        await mounted.render(<CertificatesSetting />);
        await settle();
        await act(async () => { mounted.button('+ cert_add')!.click(); });
        const row = $('[data-testid="certificate-0"]')!.getAttribute('data-row')!;
        await act(async () => { setSelectValue($<HTMLSelectElement>(`[id="certificates.${row}.typeCode-input"]`)!, 'Custom'); });
        await act(async () => { setValue($<HTMLInputElement>(`[id="certificates.${row}.customTypeName-input"]`)!, 'Demeter'); });
        await act(async () => { setValue($<HTMLInputElement>(`[id="certificates.${row}.number-input"]`)!, 'D-1'); });
        const input = $<HTMLInputElement>('[data-testid="certificate-0-file"]')!;
        Object.defineProperty(input, 'files', { value: [new globalThis.File(['%PDF-1.4'], 'd.pdf', { type: 'application/pdf' })], configurable: true });
        await act(async () => { input.dispatchEvent(new window.Event('change', { bubbles: true })); });
        await act(async () => { $<HTMLButtonElement>('[data-testid="own-certificates-save"]')!.click(); });
        await mounted.waitFor(() => $<HTMLTextAreaElement>('#certificate-custom-types')!.value.includes('Demeter'), 'own type in the settings field');
        assert.equal(mounted.hasText('cert_own_saved'), false, 'the upload failed, so no "saved" notice');
    });
});
