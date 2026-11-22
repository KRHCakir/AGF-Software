import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import CertificateAiReader from './CertificateAiReader';
import CertificatesEditor from './CertificatesEditor';
import { useCertificates } from './useCertificates';

// Certificaten deel 2: in het blok Certificaten een PDF laten lezen. Het voorstel staat naast het document; Lettuce (pagina 1) en Arugula
// (bijlage) zijn gekoppeld aan een eigen product, Mizuna niet; de gebruiker koppelt het zelf via het zoekveld. Goedkeuren slaat op via het
// gewone opslaan met aiApproval; Afwijzen sluit het voorstel zonder iets op te slaan; bij hetzelfde nummer wordt het een nieuwe versie.
const reference = {
    types: [{ code: 'GlobalGapIfa', name: 'GLOBALG.A.P. IFA (GGN)', description: '', typicalHolder: '', productsRequired: true, ggnNumber: true }],
    customTypes: [], warningDays: 30, globalGapPortalUrl: 'https://example.org', globalGapDatabaseUrl: 'https://example.org', maxFileBytes: 10 * 1024 * 1024,
};
const products = [{ id: 10, name: 'Sla' }, { id: 11, name: 'Rucola' }, { id: 12, name: 'Bladgroente mix' }];
const model = { id: 'claude-haiku-5-5', name: 'Claude Haiku 5.5', inputPerMillionUsd: 0.1, outputPerMillionUsd: 0.5, priceCheckedOn: '2026-11-11' };
const status = { enabled: true, configured: true, notConfiguredReason: null, provider: 'Anthropic', primaryModel: model, fallbackEnabled: false, fallbackModel: null,
    monthSpentEur: 0, monthlyLimitEur: 25, limitReached: false, maxFileBytes: 10 * 1024 * 1024 };
const stored = {
    id: 7, typeCode: 'GlobalGapIfa', customTypeName: null, typeName: 'GGN', number: '4063061591012', certificationBody: 'Oud', validFrom: '2025-03-01', validUntil: '2026-02-28',
    products: [{ id: 10, name: 'Sla' }], productsPending: false, producerName: 'Verde', hasGgnLabel: true, checkedOn: null, checkedBy: null, isActive: true,
    status: 'Expired', daysLeft: -200, files: [],
};
const extraction = (over: object = {}) => ({
    id: 41, status: 'Proposed', relationId: 5, fileName: 'certificaat.pdf', mediaType: 'application/pdf', displayMediaType: 'application/pdf', sizeBytes: 2000,
    provider: 'Anthropic', model: 'claude-haiku-5-5', modelName: 'Claude Haiku 5.5', modelVersion: 'x', usedFallback: false, costEur: 0.001,
    proposal: {
        typeCode: 'GlobalGapIfa', customTypeName: null, documentTypeName: 'GLOBALG.A.P. IFA', number: '4063061591012', holderName: 'Kwekerij Verde Demo S.L.', holderCountry: 'ES',
        certificationBody: 'Demo Certificering B.V.', validFrom: '2026-03-01', validUntil: '2027-02-28', status: 'Valid', daysLeft: 99,
        products: [
            { text: 'Lettuce', page: 1, productId: 10, productName: 'Sla', match: 'Translated' },
            { text: 'Arugula', page: 2, productId: 11, productName: 'Rucola', match: 'Translated' },
            { text: 'Mizuna', page: 2, productId: null, productName: null, match: 'None' },
        ],
        sites: [{ name: 'Finca Demo Norte', country: 'ES' }], holderMismatch: null, sameNumberCertificateId: null, sameNumberTypeName: null,
    },
    problems: [{ field: 'validUntil', code: 'uncertain' }, { field: 'products[2]', code: 'unmatched' }],
    ...over,
});

let mounted: Mounted;
let calls: { method: string; url: string; body: string }[];
let list: object[];
let read: object;

function Harness() {
    const certificates = useCertificates({ kind: 'relation', id: 5 });
    if (certificates.reference === null) return null;
    return (
        <>
            <CertificateAiReader owner={{ kind: 'relation', id: 5 }} certificates={certificates} blockedReason={null} dataTour="relation-certificate" />
            <CertificatesEditor idPrefix="c" rows={certificates.rows} onChange={certificates.setRows} reference={certificates.reference} products={certificates.products} errors={{}}
                today="2026-11-21" warningDays={30} onOpenFile={() => {}} onAddFile={() => {}} />
        </>
    );
}

const $ = <T extends Element = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const click = (selector: string) => act(async () => { $(selector)!.click(); });
const upload = async () => {
    const input = $<HTMLInputElement>('[data-testid="ai-upload-input"]')!;
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(2000)], 'certificaat.pdf', { type: 'application/pdf' })], configurable: true });
    await act(async () => { input.dispatchEvent(new window.Event('change', { bubbles: true })); });
    await mounted.waitFor(() => $('[data-testid="cert-ai-review"]') !== null, 'review');
};

describe('certificate AI reader', () => {
    beforeEach(() => {
        calls = [];
        list = [];
        read = extraction();
        globalThis.URL.createObjectURL = () => 'blob:x';
        globalThis.URL.revokeObjectURL = () => {};
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            const method = init?.method ?? 'GET';
            calls.push({ method, url, body: typeof init?.body === 'string' ? init.body : '' });
            if (url.includes('/certificates/reference')) return json(200, reference);
            if (url.endsWith('/products')) return json(200, products);
            if (url.includes('/certificates/ai/status')) return json(200, status);
            if (url.includes('/certificates/ai/extractions/41/document')) return new Response(new Blob(['%PDF-1.7']), { status: 200, headers: { 'Content-Type': 'application/pdf' } });
            if (url.includes('/certificates/ai/extractions/41/close')) return json(200, {});
            if (url.includes('/certificates/ai/extractions')) return json(200, read);
            if (url.includes('/certificates/relations/5') && method === 'PUT') {
                const body = JSON.parse(String(init!.body));
                list = body.certificates.map((c: { id: number | null; productIds: number[] }, i: number) => ({ ...stored, ...c, id: c.id ?? 100 + i, typeName: 'GGN', products: c.productIds.map((id: number) => ({ id, name: '' })), status: 'Valid', daysLeft: 99,
                    files: [{ id: 9, version: 1, fileName: 'certificaat.pdf', mediaType: 'application/pdf', sizeBytes: 2000, sha256: 'h', createdAt: '2026-11-21T10:00:00Z', createdBy: 'Piet',
                        ai: { approvedBy: 'Piet', approvedAt: '2026-11-21T10:00:00Z', model: 'claude-haiku-5-5', modelName: 'Claude Haiku 5.5', modelVersion: 'x', adjustedFields: 1 } }] }));
                return json(200, { version: 'v2', warningDays: 30, certificates: list, warnings: [] });
            }
            if (url.includes('/certificates/relations/5')) return json(200, { version: list.length === 0 ? '0' : 'v1', warningDays: 30, certificates: list, warnings: [] });
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the proposal sits next to the document with yellow doubt; Mizuna is linked by the user; approve saves with aiApproval', async () => {
        await mounted.render(<Harness />);
        await mounted.waitFor(() => $('[data-testid="ai-upload-button"]') !== null && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload ready');
        await upload();
        const review = $('[data-testid="cert-ai-review"]')!;
        assert.ok(review.className.includes('lg:grid-cols-2'), 'document and proposal side by side on a large screen, stacked on a phone');
        assert.ok($('[data-testid="ai-document-pdf"]') || $('[data-tour="relation-certificate-ai-document"]'), 'the document is shown');
        assert.equal($<HTMLInputElement>('[data-testid="cert-ai-number"]')!.value, '4063061591012');
        assert.equal($('[data-testid="cert-ai-mark-validUntil"]')!.textContent, 'cert_ai_doubt');
        assert.ok($('[data-testid="cert-ai-product-0"]')!.textContent!.includes('Lettuce'));
        assert.ok($('[data-testid="cert-ai-product-0"]')!.textContent!.includes('Sla'));
        assert.ok($('[data-testid="cert-ai-product-2"]')!.textContent!.includes('cert_ai_unlinked'));
        assert.ok($('[data-testid="cert-ai-sites"]')!.textContent!.includes('Finca Demo Norte (ES)'));
        await act(async () => { setValue($<HTMLInputElement>('[data-testid="cert-ai-link-2"]')!, 'Bladgroente mix'); });
        assert.ok($('[data-testid="cert-ai-product-2"]')!.textContent!.includes('Bladgroente mix'));
        assert.equal(calls.filter((c) => c.method === 'PUT').length, 0, 'nothing is saved before Goedkeuren');

        await click('[data-testid="cert-ai-approve"]');
        await mounted.waitFor(() => $('[data-testid="cert-ai-notice"]') !== null, 'approved');
        const put = JSON.parse(calls.find((c) => c.method === 'PUT')!.body);
        assert.deepEqual(put.aiApproval, { extractionId: 41, certificateIndex: 0 });
        assert.deepEqual([put.certificates[0].number, put.certificates[0].productIds, put.certificates[0].producerName], ['4063061591012', [10, 11, 12], 'Kwekerij Verde Demo S.L.']);
        assert.equal($('[data-testid="cert-ai-notice"]')!.textContent, 'cert_ai_approved');
        assert.equal($('[data-testid="certificate-0-file-ai"]')!.textContent!.startsWith('cert_file_ai'), true, 'the version says it is an approved AI proposal');
    });

    test('the same number proposes a new version; a holder of another company warns; rejecting saves nothing', async () => {
        list = [stored];
        read = extraction({ proposal: { ...extraction().proposal, holderMismatch: 'Agrícola Sol S.A.', sameNumberCertificateId: 7, sameNumberTypeName: 'GLOBALG.A.P. IFA (GGN)' } });
        await mounted.render(<Harness />);
        await mounted.waitFor(() => $('[data-testid="ai-upload-button"]') !== null && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload ready');
        await upload();
        assert.ok($('[data-testid="cert-ai-holder-mismatch"]')!.textContent!.includes('cert_ai_holder_mismatch'));
        const radios = Array.from(mounted.container.querySelectorAll<HTMLInputElement>('[data-testid="cert-ai-same-number"] input[type="radio"]'));
        assert.deepEqual(radios.map((r) => r.checked), [true, false], 'a new version is the proposal');
        await click('[data-testid="cert-ai-reject"]');
        await mounted.waitFor(() => $('[data-testid="cert-ai-notice"]') !== null, 'rejected');
        assert.deepEqual(JSON.parse(calls.find((c) => c.url.includes('/close'))!.body), { outcome: 'Rejected' });
        assert.equal(calls.filter((c) => c.method === 'PUT').length, 0);
        assert.equal($('[data-testid="cert-ai-review"]'), null);
    });

    test('approving a new version sends the stored certificate with its id and the new end date', async () => {
        list = [stored];
        read = extraction({ proposal: { ...extraction().proposal, sameNumberCertificateId: 7, sameNumberTypeName: 'GLOBALG.A.P. IFA (GGN)' } });
        await mounted.render(<Harness />);
        await mounted.waitFor(() => $('[data-testid="ai-upload-button"]') !== null && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload ready');
        await upload();
        await click('[data-testid="cert-ai-approve"]');
        await mounted.waitFor(() => calls.some((c) => c.method === 'PUT'), 'PUT');
        const put = JSON.parse(calls.find((c) => c.method === 'PUT')!.body);
        assert.equal(put.certificates.length, 1);
        assert.deepEqual([put.certificates[0].id, put.certificates[0].validUntil, put.aiApproval.certificateIndex], [7, '2027-02-28', 0]);
    });

    test('Handmatig verder puts the values and the document as an open change in the block; nothing is uploaded or saved yet', async () => {
        list = [stored];
        read = extraction({ proposal: { ...extraction().proposal, sameNumberCertificateId: 7, sameNumberTypeName: 'GLOBALG.A.P. IFA (GGN)' } });
        await mounted.render(<Harness />);
        await mounted.waitFor(() => $('[data-testid="ai-upload-button"]') !== null && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload ready');
        await upload();
        await click('[data-testid="cert-ai-manual"]');
        await mounted.waitFor(() => $('[data-testid="cert-ai-notice"]') !== null, 'manual');
        assert.deepEqual(JSON.parse(calls.find((c) => c.url.includes('/close'))!.body), { outcome: 'Manual' });
        assert.equal(calls.filter((c) => c.method === 'PUT' || (c.method === 'POST' && c.url.includes('/files'))).length, 0, 'nothing saved or uploaded');
        assert.ok($('[data-testid="certificate-0-files"]')!.textContent!.includes('cert_file_pending'), 'the document waits as an open file on the existing certificate');
    });
});
