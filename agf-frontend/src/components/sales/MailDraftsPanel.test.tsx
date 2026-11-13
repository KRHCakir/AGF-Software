import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { MailDraft } from '../../types/documentMail';
import type { SalesOrder } from '../../types/salesOrder';
import MailDraftsPanel from './MailDraftsPanel';

// Documenten deel 3: het paneel Te versturen op de order. Niets gaat weg zonder een klik op Versturen; aanpassingen worden eerst opgeslagen;
// veel ontvangers vragen een tweede klik; zonder adres een melding met een link; zonder afzender PDF downloaden en tekst kopiëren.

const order = (over: Partial<SalesOrder> = {}) => ({ id: 12, orderNumber: 'VO-2026-0012', status: 'Open', version: 3, ...over }) as unknown as SalesOrder;
const draft = (over: Partial<MailDraft> = {}): MailDraft => ({
    id: 40, salesOrderId: 12, orderNumber: 'VO-2026-0012', customerName: 'Groothandel Noord', documentId: 101, documentKind: 'TransportOrder', documentNumber: 'TO-2026-0012',
    documentVersion: 1, status: 'Draft', to: 'planning@zeeland.nl', cc: null, subject: 'Transportopdracht TO-2026-0012', body: 'Beste,\n\nIn de bijlage…', missingRecipient: null,
    missingRelationId: null, previousSentAt: null, createdAt: '2026-11-13T08:00:00Z', sentAt: null, sentBy: null, messageId: null, lastError: null, attempts: 0, version: 1, ...over,
});

let mounted: Mounted;
let calls: { method: string; url: string; body: Record<string, unknown> | null }[];
let answer: (method: string, url: string, body: Record<string, unknown> | null) => Response;
const $ = <T extends HTMLElement = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
const posts = () => calls.filter((call) => call.method === 'POST');

describe('mail drafts panel', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const body = init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null;
            calls.push({ method: init?.method ?? 'GET', url: String(input), body });
            return answer(init?.method ?? 'GET', String(input), body);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('loading sends nothing; an edit is saved first, then sent with the new version; the sent line says when and to whom', async () => {
        let sent = false;
        answer = (method, url) => {
            if (url.endsWith('/mail-drafts/status')) return json(200, { configured: true, sender: 'Fake' });
            if (url.endsWith('/sales-orders/12/mail-drafts')) return json(200, [sent ? draft({ status: 'Sent', sentAt: '2026-11-13T09:00:00Z', sentBy: 'Admin', to: 'planning@zeeland.nl, chef@zeeland.nl' }) : draft()]);
            if (method === 'PUT') return json(200, draft({ version: 2, to: 'planning@zeeland.nl, chef@zeeland.nl' }));
            if (url.endsWith('/send')) { sent = true; return json(200, draft({ status: 'Sent' })); }
            return json(404, {});
        };
        await mounted.render(<MailDraftsPanel order={order()} />);
        await settle();
        assert.equal(posts().length, 0, 'nothing is sent by showing the draft');
        assert.ok($('[data-testid="mail-fake-sender"]'));
        await act(async () => { setValue($<HTMLInputElement>('#mail-40-to')!, 'planning@zeeland.nl, chef@zeeland.nl'); });
        await act(async () => { $('[data-tour="so-mail-send"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-sent"]'), 'sent line');
        assert.deepEqual(calls.filter((c) => c.method !== 'GET').map((c) => `${c.method} ${c.url.replace(/^.*\/api/, '')}`), ['PUT /mail-drafts/40', 'POST /mail-drafts/40/send']);
        assert.deepEqual(posts()[0].body, { expectedVersion: 2, confirmManyRecipients: false, confirmResend: false });
        assert.ok($('[data-testid="mail-sent"]')!.textContent!.includes('mail_sent_line'));
    });

    test('more than ten recipients needs a second click; a failed send shows the reason and can be sent again', async () => {
        let attempt = 0;
        answer = (method, url, body) => {
            if (url.endsWith('/mail-drafts/status')) return json(200, { configured: true, sender: 'Smtp' });
            if (url.endsWith('/sales-orders/12/mail-drafts')) return json(200, [attempt >= 2 ? draft({ status: 'Failed', lastError: 'AuthenticationException: 535', version: 3 }) : draft()]);
            if (url.endsWith('/send')) {
                attempt++;
                if (!body!.confirmManyRecipients) return json(409, { code: 'confirmRecipients', count: 12, message: 'confirm' });
                return json(200, draft({ status: 'Failed' }));
            }
            return json(404, {});
        };
        await mounted.render(<MailDraftsPanel order={order()} />);
        await settle();
        await act(async () => { $('[data-tour="so-mail-send"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-confirm"]'), 'confirm');
        assert.ok($('[data-testid="mail-confirm"]')!.textContent!.includes('mail_confirm_many'));
        await act(async () => { $('[data-tour="so-mail-confirm"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-failed"]'), 'failed');
        assert.equal(posts().filter((p) => p.url.endsWith('/send')).length, 2);
        assert.ok($('[data-tour="so-mail-send"]'), 'can be sent again');
    });

    test('after an edit is saved, the confirmations use the new version and are sent together (unknown outcome and many recipients)', async () => {
        let sent = false;
        answer = (method, url, body) => {
            if (url.endsWith('/mail-drafts/status')) return json(200, { configured: true, sender: 'Smtp' });
            if (url.endsWith('/sales-orders/12/mail-drafts')) return json(200, [sent ? draft({ status: 'Sent', sentAt: '2026-11-13T09:00:00Z', sentBy: 'Admin', version: 3 }) : draft()]);
            if (method === 'PUT') return json(200, draft({ version: 2, subject: 'Transportopdracht TO-2026-0012 (spoed)' }));
            if (url.endsWith('/send')) {
                if (body!.expectedVersion !== 2) return json(409, { code: 'stale', message: 'stale' });
                if (!body!.confirmResend) return json(409, { code: 'uncertain', message: 'uncertain' });
                if (!body!.confirmManyRecipients) return json(409, { code: 'confirmRecipients', count: 12, message: 'confirm' });
                sent = true;
                return json(200, draft({ status: 'Sent' }));
            }
            return json(404, {});
        };
        await mounted.render(<MailDraftsPanel order={order()} />);
        await settle();
        await act(async () => { setValue($<HTMLInputElement>('#mail-40-subject')!, 'Transportopdracht TO-2026-0012 (spoed)'); });
        await act(async () => { $('[data-tour="so-mail-send"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-confirm"]'), 'resend question');
        assert.ok($('[data-testid="mail-confirm"]')!.textContent!.includes('mail_confirm_resend'));
        await act(async () => { $('[data-tour="so-mail-confirm"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-confirm"]')?.textContent?.includes('mail_confirm_many'), 'many question');
        await act(async () => { $('[data-tour="so-mail-confirm"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-sent"]'), 'sent line');
        assert.equal(calls.filter((c) => c.method === 'PUT').length, 1, 'the edit is saved once');
        assert.deepEqual(posts().map((p) => p.body), [
            { expectedVersion: 2, confirmManyRecipients: false, confirmResend: false },
            { expectedVersion: 2, confirmManyRecipients: false, confirmResend: true },
            { expectedVersion: 2, confirmManyRecipients: true, confirmResend: true },
        ]);
    });

    test('a version conflict offers a refresh: the edit is kept with the new version, and the retry succeeds', async () => {
        const theirs = draft({ version: 5, subject: 'Hun onderwerp' });
        answer = (method, url, body) => {
            if (url.endsWith('/mail-drafts/status')) return json(200, { configured: true, sender: 'Smtp' });
            if (url.endsWith('/sales-orders/12/mail-drafts')) return json(200, [calls.some((c) => c.method === 'PUT') ? theirs : draft()]);
            if (method === 'PUT') return body!.expectedVersion === 5 ? json(200, draft({ version: 6, subject: 'Mijn onderwerp' })) : json(409, { code: 'stale', message: 'stale' });
            return json(404, {});
        };
        await mounted.render(<MailDraftsPanel order={order()} />);
        await settle();
        await act(async () => { setValue($<HTMLInputElement>('#mail-40-subject')!, 'Mijn onderwerp'); });
        await act(async () => { $('[data-tour="so-mail-send"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-conflict"]'), 'conflict notice');
        await act(async () => { $('[data-testid="mail-conflict-compare"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="mail-conflict-current"]'), 'current version');
        assert.ok($('[data-testid="mail-conflict-current"]')!.textContent!.includes('Hun onderwerp'));
        await act(async () => { $('[data-testid="mail-conflict-keep"]')!.click(); });
        await settle();
        assert.equal($('[data-testid="mail-conflict"]'), null);
        assert.equal($<HTMLInputElement>('#mail-40-subject')!.value, 'Mijn onderwerp', 'the unsaved edit is kept');
        await act(async () => { $('[data-tour="so-mail-send"]')!.click(); });
        await settle();
        assert.equal(calls.filter((c) => c.method === 'PUT').at(-1)!.body!.expectedVersion, 5);
    });

    test('without an address a notice links to the relation; without a sender the PDF and the text are the fallback and Send is not offered', async () => {
        answer = (method, url) => {
            if (url.endsWith('/mail-drafts/status')) return json(200, { configured: false, sender: 'Smtp' });
            if (url.endsWith('/sales-orders/12/mail-drafts')) return json(200, [draft({ to: null, missingRecipient: 'Transport Zeeland B.V.', missingRelationId: 7, previousSentAt: '2026-11-12T10:00:00Z' })]);
            return json(404, {});
        };
        await mounted.render(<MailDraftsPanel order={order()} />);
        await settle();
        assert.ok($('[data-testid="mail-missing"]')!.textContent!.includes('mail_missing_recipient'));
        assert.equal($<HTMLAnchorElement>('[data-tour="so-mail-add-address"]')!.getAttribute('href'), '/relations?open=7&section=recipients');
        assert.ok(mounted.hasText('mail_previous_sent'));
        assert.ok($('[data-testid="mail-not-configured"]'));
        assert.equal($('[data-tour="so-mail-send"]'), null);
        assert.ok($('[data-testid="mail-download"]'));
        let copied = '';
        Object.defineProperty(globalThis.navigator, 'clipboard', { value: { writeText: async (text: string) => { copied = text; } }, configurable: true });
        await act(async () => { $('[data-testid="mail-copy"]')!.click(); });
        await settle();
        assert.ok(copied.startsWith('Transportopdracht TO-2026-0012\n\nBeste,'));
    });
});
