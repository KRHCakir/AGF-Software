import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setSelectValue, setValue } from '../../test/reactHarness.ts';
import { type CertificateRow, emptyCertificateRow } from '../../lib/certificateRules';
import type { CertificateReference } from '../../types/certificate';
import CertificatesEditor from './CertificatesEditor';

// Het blok "Certificaten": soort uit de lijst of een eigen soort, meerdere producten, de status uit de einddatum, waarschuwingen zonder
// blokkade, een bestand bij een nieuw certificaat gaat mee met Opslaan, en Tab op het laatste veld (Actief) geeft een nieuwe regel.

const REFERENCE: CertificateReference = {
    types: [
        { code: 'GlobalGapIfa', name: 'GLOBALG.A.P. IFA (GGN)', description: '', typicalHolder: '', productsRequired: true, ggnNumber: true },
        { code: 'Skal', name: 'SKAL / EU Organic (bio)', description: '', typicalHolder: '', productsRequired: false, ggnNumber: false },
    ],
    customTypes: ['Demeter'], warningDays: 30, globalGapPortalUrl: 'https://portal.example', globalGapDatabaseUrl: 'https://database.example', maxFileBytes: 10_000_000,
};
const PRODUCTS = [{ id: 1, name: 'Avocado' }, { id: 2, name: 'Limoen' }, { id: 3, name: 'Mango' }];

let mounted: Mounted;
let latest: CertificateRow[] = [];
const added: { key: string; name: string }[] = [];
function Harness({ initial }: { initial: CertificateRow[] }) {
    const [rows, setRows] = useState(initial);
    latest = rows;
    return <CertificatesEditor idPrefix="c" rows={rows} onChange={setRows} reference={REFERENCE} products={PRODUCTS} errors={{}} today="2026-11-20" warningDays={30}
        onOpenFile={() => {}} onAddFile={(row, file) => { added.push({ key: row.key, name: file.name }); setRows(rows.map((r) => (r.key === row.key ? { ...r, pendingFiles: [...r.pendingFiles, file] } : r))); }} />;
}
const tab = async (element: HTMLElement) => {
    const event = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    await act(async () => { element.dispatchEvent(event); });
    return event.defaultPrevented;
};
const select = async (element: HTMLSelectElement, value: string) => { await act(async () => { setSelectValue(element, value); }); };

describe('CertificatesEditor', () => {
    beforeEach(() => { mounted = new Mounted(); latest = []; added.length = 0; });
    afterEach(async () => { await mounted.unmount(); });

    test('a GGN for several products, with status, warning and the portal button; Tab on Active adds a row', async () => {
        await mounted.render(<Harness initial={[emptyCertificateRow()]} />);
        const row = latest[0];
        const type = mounted.container.querySelector<HTMLSelectElement>(`#certificates\\.${row.key}\\.typeCode-input`)!;
        await select(type, 'GlobalGapIfa');
        assert.ok(mounted.hasText('cert_number_ggn'));
        assert.ok(mounted.hasText('cert_type_info_GlobalGapIfa'));
        assert.ok(mounted.container.querySelector('[data-testid="certificate-0-warning-products_missing"]'), 'a GGN without products warns');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>(`#certificates\\.${row.key}\\.number-input`)!, '4049928415685'); });
        assert.ok(mounted.container.querySelector('[data-testid="certificate-0-warning-ggn_check_digit"]'));
        const product = mounted.container.querySelector<HTMLSelectElement>(`#certificates\\.${row.key}\\.productIds-input`)!;
        await select(product, '3');
        await select(product, '2');
        assert.deepEqual(latest[0].productIds, [3, 2]);
        assert.equal(mounted.container.querySelector('[data-testid="certificate-0-warning-products_missing"]'), null);
        assert.deepEqual(Array.from(product.options).map((o) => o.value), ['', '1'], 'chosen products leave the list');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>(`#certificates\\.${row.key}\\.validUntil-input`)!, '10-12-26'); });
        await act(async () => { mounted.container.querySelector<HTMLInputElement>(`#certificates\\.${row.key}\\.validUntil-input`)!.dispatchEvent(new window.FocusEvent('blur', { bubbles: true })); });
        assert.equal(latest[0].validUntil, '2026-12-10', 'a two-digit year becomes 2026');
        assert.equal(mounted.container.querySelector('[data-testid="certificate-0-status"]')!.getAttribute('data-status'), 'ExpiringSoon');
        assert.ok(mounted.button('relations_ggn_check'));

        assert.equal(await tab(mounted.container.querySelector<HTMLInputElement>('[data-testid="certificate-0-active"]')!), true);
        assert.equal(latest.length, 2);
        assert.equal(window.document.activeElement, mounted.container.querySelector(`#certificates\\.${latest[1].key}\\.typeCode-input`));
        assert.equal(await tab(mounted.container.querySelector<HTMLInputElement>('[data-testid="certificate-1-active"]')!), false, 'the new row is still blank');
    });

    test('an own type from the list or a new one, and a file for a new certificate waits for Save', async () => {
        await mounted.render(<Harness initial={[emptyCertificateRow()]} />);
        const row = latest[0];
        const type = mounted.container.querySelector<HTMLSelectElement>(`#certificates\\.${row.key}\\.typeCode-input`)!;
        await select(type, 'custom:Demeter');
        assert.deepEqual([latest[0].typeCode, latest[0].customTypeName], ['Custom', 'Demeter']);
        assert.equal(mounted.container.querySelector(`#certificates\\.${row.key}\\.customTypeName-input`), null, 'a known own type needs no name field');
        await select(type, 'Custom');
        assert.ok(mounted.container.querySelector(`#certificates\\.${row.key}\\.customTypeName-input`));
        const input = mounted.container.querySelector<HTMLInputElement>('[data-testid="certificate-0-file"]')!;
        const file = new window.File(['%PDF-1.4'], 'skal.pdf', { type: 'application/pdf' });
        Object.defineProperty(input, 'files', { value: [file], configurable: true });
        await act(async () => { input.dispatchEvent(new window.Event('change', { bubbles: true })); });
        assert.deepEqual(added, [{ key: row.key, name: 'skal.pdf' }]);
        assert.ok(mounted.hasText('cert_file_pending'));
    });
});
