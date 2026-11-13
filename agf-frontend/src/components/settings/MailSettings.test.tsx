import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import MailSenderSetting from './MailSenderSetting';
import MailTemplatesSetting from './MailTemplatesSetting';
import WarehouseRecipientsSetting from './WarehouseRecipientsSetting';

// Documenten deel 3 onder Instellingen: de afzender (het wachtwoord gaat er alleen in), de e-mailadressen van de loods en de sjablonen.

let mounted: Mounted;
let calls: { method: string; url: string; body: Record<string, unknown> | null }[];
let answer: (method: string, url: string, body: Record<string, unknown> | null) => Response;
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
const puts = () => calls.filter((call) => call.method === 'PUT');
const settings = (over: Record<string, unknown> = {}) => ({ host: 'smtp.voorbeeld.nl', port: 587, security: 'StartTls', userName: 'verkoop', password: null, clearPassword: false, hasPassword: true,
    fromAddress: 'verkoop@voorbeeld.nl', fromName: 'Verkoop', signature: null, sender: 'Smtp', configured: true, ...over });

describe('mail settings', () => {
    beforeEach(() => {
        calls = [];
        answer = () => json(404, {});
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const body = init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null;
            calls.push({ method: init?.method ?? 'GET', url: String(input), body });
            return answer(init?.method ?? 'GET', String(input), body);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the password is never shown: empty means unchanged, the box clears it; the simulated sender says no real mail goes out', async () => {
        answer = (method, url, body) => json(200, settings(method === 'PUT' ? { port: body!.port, hasPassword: !body!.clearPassword } : {}));
        await mounted.render(<MailSenderSetting />);
        await settle();
        const password = mounted.container.querySelector<HTMLInputElement>('#mail-password')!;
        assert.deepEqual([password.value, password.type, password.placeholder], ['', 'password', 'mail_password_set']);
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#mail-port')!, '465'); });
        await submit();
        await mounted.waitFor(() => puts().length === 1, 'saved');
        assert.deepEqual([puts()[0].body!.password, puts()[0].body!.clearPassword, puts()[0].body!.port], [null, false, 465]);
        await act(async () => { mounted.container.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click(); });
        await submit();
        await mounted.waitFor(() => puts().length === 2, 'saved again');
        assert.equal(puts()[1].body!.clearPassword, true);

        await mounted.unmount();
        mounted = new Mounted();
        answer = () => json(200, settings({ sender: 'Fake', host: null, hasPassword: false }));
        await mounted.render(<MailSenderSetting />);
        await settle();
        assert.ok(mounted.container.querySelector('[data-testid="mail-sender-fake"]'));
    });

    test('the warehouse addresses are saved with the version seen; templates save and go back to the standard text', async () => {
        answer = (method) => json(200, method === 'PUT'
            ? { version: 'v2', recipients: [{ id: 1, name: 'Loods', email: 'loods@voorbeeld.nl', role: 'To', kinds: ['LoadingSlip'], relationAddressId: null }], addresses: [], warnings: [] }
            : { version: '0', recipients: [], addresses: [], warnings: [] });
        await mounted.render(<WarehouseRecipientsSetting />);
        await settle();
        await act(async () => { mounted.button('+ recipients_add')!.click(); });
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#warehouse-0-name')!, 'Loods'); });
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#warehouse-0-email')!, 'loods@voorbeeld.nl'); });
        await submit();
        await mounted.waitFor(() => puts().length === 1, 'saved');
        assert.deepEqual(puts()[0].body, { expectedVersion: '0', recipients: [{ id: null, name: 'Loods', email: 'loods@voorbeeld.nl', role: 'To', kinds: ['LoadingSlip'], relationAddressId: null }] });

        await mounted.unmount();
        mounted = new Mounted();
        calls = [];
        const templates = (own: boolean) => ({ placeholders: ['{order}'], templates: [{ kind: 'LoadingSlip', subject: own ? 'Eigen' : 'Laadbon {nummer}', body: 'Tekst', isDefault: !own }] });
        answer = (method, url, body) => json(200, templates(method === 'PUT' && body!.subject !== ''));
        await mounted.render(<MailTemplatesSetting />);
        await settle();
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#template-LoadingSlip-subject')!, 'Eigen'); });
        await act(async () => { mounted.button('loading_slip_save')!.click(); });
        await mounted.waitFor(() => mounted.button('mail_template_reset') !== null, 'own template');
        assert.deepEqual(puts()[0].body, { kind: 'LoadingSlip', subject: 'Eigen', body: 'Tekst' });
        await act(async () => { mounted.button('mail_template_reset')!.click(); });
        await mounted.waitFor(() => puts().length === 2, 'reset');
        assert.deepEqual([puts()[1].body!.subject, puts()[1].body!.body], ['', '']);
    });
});
