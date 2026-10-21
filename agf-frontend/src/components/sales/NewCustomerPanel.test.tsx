import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { StrictMode, act } from 'react';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands } from '../../test/reactHarness.ts';
import { CREATE_CUSTOMER_KEY, PendingCommand } from '../../lib/pendingCommands';
import { CustomerOption, ProductOption } from '../../types/referenceData';
import CreateSalesOrderForm from './CreateSalesOrderForm';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Nieuwe klant vanuit het orderformulier: het orderconcept blijft staan bij openen, sluiten en opslaan; gelijkende namen
// zijn een definitieve afwijzing waarna "Toch aanmaken" een nieuwe opdracht is; netwerkfouten houden de identieke opdracht
// vast (ook na sluiten en opnieuw opbouwen); na ontkoppelen wordt niets in een ander concept gekozen.

const customers: CustomerOption[] = [{ id: 1, name: 'Local Market Stalls' }, { id: 2, name: 'DEMO Klant B' }];
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: '4kg Box', sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null }] }];
const created = { id: 7, name: 'Groentehal Jansen', contactPerson: 'Piet', email: null, phone: '06-1', shippingAddress: null, vatNumber: null, billingAddress: null, paymentTermsDays: 14, createdAt: '2026-10-19T14:00:00.000Z' };

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;

const element = (onCustomerCreated?: () => void) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <CreateSalesOrderForm actor="office-1" customers={customers} products={products} onSaved={() => {}} onOpen={() => {}} onCancel={() => {}} onCustomerCreated={onCustomerCreated} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const orderForm = () => mounted.container.querySelector<HTMLFormElement>('form')!;
const panel = () => mounted.container.querySelector<HTMLElement>('[data-testid="new-customer-panel"]');
const savePanel = () => click(mounted.buttons().find((b) => b.textContent === 'sales_customer_save')!);
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const customerSelect = () => mounted.container.querySelector<HTMLSelectElement>('#customerId-input')!;
const newCustomerButton = () => mounted.buttons().find((b) => (b.textContent ?? '').includes('sales_new_customer') && !(b.textContent ?? '').includes('title'))!;

async function typeDraft() {
    await act(async () => {
        setValue(input('customerReference-input'), 'PO-77');
        setValue(input('requestedDeliveryDate-input'), '2026-10-30');
        setValue(input('lines[0].quantity-input'), '12');
    });
}
function assertDraftIntact() {
    assert.equal(input('customerReference-input').value, 'PO-77');
    assert.equal(input('requestedDeliveryDate-input').value, '2026-10-30');
    assert.equal(input('lines[0].quantity-input').value, '12');
}

describe('NewCustomerPanel inside CreateSalesOrderForm', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        nextPost = async () => json(500, { message: 'no post handler' });
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: () => nextPost() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('open, close and create keep the order draft; the new customer is selected and offered in the list', async () => {
        let refreshed = 0;
        await mounted.render(element(() => { refreshed += 1; }));
        await mounted.waitFor(() => orderForm(), 'order form');
        await typeDraft();
        await click(newCustomerButton());
        assert.ok(panel(), 'panel open');
        assertDraftIntact();
        await click(mounted.buttons().find((b) => b.textContent === 'sales_close_form' && b.closest('[data-testid="new-customer-panel"]') === panel())!);
        assert.equal(panel(), null);
        assertDraftIntact();
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), ' Groentehal Jansen '); setValue(input('customer-phone-input'), '06-1'); });
        nextPost = async () => json(201, created);
        await savePanel();
        await mounted.waitFor(() => panel() === null, 'panel closes after success');
        const body = JSON.parse(posted[0].body);
        assert.equal(posted[0].url.endsWith('/customers'), true);
        assert.deepEqual({ ...body, requestId: 'x' }, { requestId: 'x', name: 'Groentehal Jansen', contactPerson: null, email: null, phone: '06-1', shippingAddress: null, acknowledgeSimilarNames: false });
        assert.equal(customerSelect().value, '7', 'created customer selected');
        assert.ok(Array.from(customerSelect().options).some((o) => o.value === '7' && o.textContent === 'Groentehal Jansen'));
        assertDraftIntact();
        assert.equal(mounted.hasText('sales_customer_created'), true);
        assert.equal(refreshed, 1);
        assert.deepEqual(storedCommands(), {});
    });

    test('a similar-name 409 is definitive: nothing stays frozen, "create anyway" sends a NEW requestId with the acknowledgement, choosing an existing customer selects it', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => orderForm(), 'order form');
        await typeDraft();
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'demo klant b'); });
        assert.equal(mounted.hasText('sales_customer_similar_live'), true, 'live warning from the loaded options');
        nextPost = async () => json(409, { message: 'similar', code: 'similar-names', similarCustomers: [{ id: 2, name: 'DEMO Klant B', contactPerson: null, phone: null }] });
        await savePanel();
        await mounted.waitFor(() => mounted.hasText('sales_customer_similar_title'), 'similar panel');
        assert.deepEqual(storedCommands(), {}, 'a definitive 409 leaves no frozen command');
        assert.equal(mounted.button('supply_retry_same'), null);
        const first = JSON.parse(posted[0].body);
        assert.equal(first.acknowledgeSimilarNames, false);
        nextPost = async () => json(201, { ...created, id: 8, name: 'demo klant b' });
        await click(mounted.button('sales_customer_create_anyway')!);
        await mounted.waitFor(() => panel() === null, 'created anyway');
        const second = JSON.parse(posted[1].body);
        assert.equal(second.acknowledgeSimilarNames, true);
        assert.notEqual(second.requestId, first.requestId, 'acknowledgement is a new command with a new requestId');
        assert.equal(second.name, first.name);
        assert.equal(customerSelect().value, '8');
        assertDraftIntact();
        // Bestaande klant kiezen vanuit de waarschuwing
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'demo klant b'); });
        nextPost = async () => json(409, { message: 'similar', code: 'similar-names', similarCustomers: [{ id: 2, name: 'DEMO Klant B', contactPerson: null, phone: null }] });
        await savePanel();
        await mounted.waitFor(() => mounted.button('sales_customer_use_existing'), 'use existing');
        await click(mounted.button('sales_customer_use_existing')!);
        assert.equal(panel(), null);
        assert.equal(customerSelect().value, '2');
        assertDraftIntact();
    });

    test('network failure freezes the identical payload; closing and reopening shows the same command; the retry resends it byte for byte', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => orderForm(), 'order form');
        await typeDraft();
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'Groentehal Jansen'); setValue(input('customer-email-input'), 'piet@jansen.example'); });
        nextPost = async () => { throw new TypeError('network down'); };
        await savePanel();
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button');
        const stored = storedCommands()[CREATE_CUSTOMER_KEY] as PendingCommand & { payload: { requestId: string } };
        assert.equal(stored.kind, 'create-customer');
        assertDraftIntact();
        // Sluiten lukt niet terwijl de opdracht openstaat: het paneel blijft, met dezelfde (vergrendelde) gegevens.
        assert.equal(input('customer-name-input').disabled, true);
        await click(mounted.buttons().find((b) => b.textContent === 'sales_close_form' && b.closest('[data-testid="new-customer-panel"]') === panel())!);
        assert.ok(panel(), 'an unresolved command keeps the panel visible');
        // Nieuwe klant-knop vervangt de opdracht niet
        await click(newCustomerButton());
        assert.deepEqual(storedCommands()[CREATE_CUSTOMER_KEY], stored);
        // Formulier opnieuw opbouwen (navigatie/verversen): zelfde opdracht komt terug met dezelfde waarden.
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(element());
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after rebuild');
        assert.equal(input('customer-name-input').value, 'Groentehal Jansen');
        nextPost = async () => json(200, created);
        await click(mounted.button('supply_retry_same')!);
        await mounted.waitFor(() => panel() === null, 'resolved');
        assert.deepEqual(JSON.parse(posted[1].body), stored.payload, 'identical payload and requestId');
        assert.deepEqual(storedCommands(), {});
        assert.equal(customerSelect().value, '7');
        assert.equal(mounted.hasText('supply_saved_before'), true);
    });

    test('under StrictMode (setup -> cleanup -> setup) a successful creation still selects the customer and closes the panel', async () => {
        await mounted.render(<StrictMode>{element()}</StrictMode>);
        await mounted.waitFor(() => orderForm(), 'order form');
        await typeDraft();
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'Groentehal Jansen'); });
        nextPost = async () => json(201, created);
        await savePanel();
        await mounted.waitFor(() => panel() === null, 'panel closes under StrictMode');
        assert.equal(customerSelect().value, '7');
        assert.equal(mounted.hasText('sales_customer_created'), true);
        assert.equal(mounted.hasText('sales_customer_saving'), false, 'not stuck in submitting state');
        assertDraftIntact();
        assert.deepEqual(storedCommands(), {});
    });

    test('Enter saves only from the text inputs; buttons keep their normal activation (choose existing, create anyway with a new requestId)', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => orderForm(), 'order form');
        await typeDraft();
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'demo klant b'); });
        const pressEnter = (target: HTMLElement) => {
            const event = new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
            target.dispatchEvent(event);
            return event.defaultPrevented;
        };
        // Enter in een tekstveld: opslaan (één verzoek), de Enter bereikt het orderformulier niet als submit.
        nextPost = async () => json(409, { message: 'similar', code: 'similar-names', similarCustomers: [{ id: 2, name: 'DEMO Klant B', contactPerson: null, phone: null }] });
        let prevented = false;
        await act(async () => { prevented = pressEnter(input('customer-name-input')); });
        await mounted.waitFor(() => mounted.hasText('sales_customer_similar_title'), 'similar panel');
        assert.equal(prevented, true, 'Enter in a text input is handled by the panel');
        assert.equal(posted.length, 1);
        assert.equal(posted[0].url.endsWith('/customers'), true, 'only the customer request was sent');
        // Enter op "Deze klant kiezen": niet onderschept; de gewone activatie (klik) kiest de bestaande klant, zonder verzoek.
        const useExisting = mounted.button('sales_customer_use_existing')!;
        await act(async () => { prevented = pressEnter(useExisting); });
        assert.equal(prevented, false, 'Enter on a button is left to the browser');
        assert.equal(posted.length, 1, 'no save triggered by Enter on the button');
        await click(useExisting);
        assert.equal(customerSelect().value, '2');
        assert.equal(panel(), null);
        // Opnieuw: Enter op "Toch aanmaken" wordt niet onderschept; de activatie stuurt een NIEUWE opdracht met bevestiging.
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'demo klant b'); });
        await savePanel();
        await mounted.waitFor(() => mounted.button('sales_customer_create_anyway'), 'create anyway button');
        const firstRequestId = JSON.parse(posted[posted.length - 1].body).requestId;
        const createAnyway = mounted.button('sales_customer_create_anyway')!;
        nextPost = async () => json(201, { ...created, id: 8, name: 'demo klant b' });
        await act(async () => { prevented = pressEnter(createAnyway); });
        assert.equal(prevented, false);
        const before = posted.length;
        await click(createAnyway);
        await mounted.waitFor(() => panel() === null, 'created anyway');
        assert.equal(posted.length, before + 1, 'exactly one request from the button activation');
        const body = JSON.parse(posted[posted.length - 1].body);
        assert.equal(body.acknowledgeSimilarNames, true);
        assert.notEqual(body.requestId, firstRequestId, 'new requestId for the acknowledged command');
        assert.equal(customerSelect().value, '8');
        assertDraftIntact();
    });

    test('a request that completes after the panel was unmounted selects nothing in the rebuilt draft', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => orderForm(), 'order form');
        await click(newCustomerButton());
        await act(async () => { setValue(input('customer-name-input'), 'Groentehal Jansen'); });
        const slow = deferred<Response>();
        nextPost = () => slow.promise;
        await savePanel();
        assert.ok(storedCommands()[CREATE_CUSTOMER_KEY], 'frozen while in flight');
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(element());
        await mounted.waitFor(() => orderForm(), 'rebuilt form');
        slow.resolve(json(201, created));
        for (let i = 0; i < 5; i++) await mounted.waitFor(() => true, 'flush');
        assert.equal(customerSelect().value, '', 'no automatic selection in the rebuilt draft');
    });
});
