'use client';

import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import CreateSalesOrderForm from '../../../components/sales/CreateSalesOrderForm';
import PendingCommandsBanner from '../../../components/sales/PendingCommandsBanner';
import { HydratedGate, PendingCommandsProvider, usePendingCommands } from '../../../components/sales/PendingCommandsContext';
import SalesOrderDetail from '../../../components/sales/SalesOrderDetail';
import SalesOrderList from '../../../components/sales/SalesOrderList';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useActorReference } from '../../../components/supply/useActorReference';
import { useTranslation } from '../../../i18n/LanguageContext';
import { CREATE_ORDER_KEY, PendingCommand } from '../../../lib/pendingCommands';
import { matchesOrderSearch } from '../../../lib/salesOrderRules';
import { toApiError } from '../../../services/apiClient';
import { getCustomers, getProductsWithVariants } from '../../../services/referenceDataService';
import { getSalesOrder, listSalesOrders } from '../../../services/salesOrderService';
import { CustomerOption, ProductOption } from '../../../types/referenceData';
import { SalesOrder } from '../../../types/salesOrder';

// Kantoorwerkplek "Verkoop": verkooporder aanmaken, exacte goedgekeurde voorraad reserveren en weer vrijgeven.
// Nieuwe flow naast de legacy-pagina Bestellingen (oud); er wordt niets gesynchroniseerd of gemigreerd.
// Buiten scope in deze PR: annuleren, DirectTrade-verkoop, dispatch, prijzen, btw en facturen.
export default function SalesOrdersPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('sales_loading')} />}>
            <PendingCommandsProvider>
                <SalesWorkspace />
            </PendingCommandsProvider>
        </Suspense>
    );
}

interface ReferenceData {
    customers: CustomerOption[];
    products: ProductOption[];
}

function parseOrderId(value: string | null): number | null {
    return value !== null && /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
}

function SalesWorkspace() {
    const { t } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const selectedId = parseOrderId(searchParams.get('orderId'));
    const { hydrated, commands } = usePendingCommands();
    const [actor, setActor] = useActorReference();
    const [reference, setReference] = useState<LoadState<ReferenceData>>({ status: 'loading' });
    const [referenceToken, setReferenceToken] = useState(0);
    const [orders, setOrders] = useState<LoadState<SalesOrder[]>>({ status: 'loading' });
    const [ordersToken, setOrdersToken] = useState(0);
    // Expliciet vernieuwen: alleen lezen. De bestaande lijst en het bestaande detail blijven staan tot er verse gegevens zijn;
    // mislukt het, dan blijft alles zoals het was met een melding. Formulieren en onafgeronde commando's worden niet aangeraakt.
    const [listRefreshing, setListRefreshing] = useState(false);
    const [listRefreshFailed, setListRefreshFailed] = useState(false);
    const [detailRefreshing, setDetailRefreshing] = useState(false);
    const [detailRefreshFailed, setDetailRefreshFailed] = useState(false);
    const [detail, setDetail] = useState<LoadState<SalesOrder> | null>(null);
    // Elke orderlezing (selectie, herladen na een actie, vernieuwen) krijgt een volgnummer. Alleen het antwoord van de
    // laatste lezing mag het detail zetten: een traag antwoord van een eerder gekozen order vervangt nooit de huidige.
    const detailRequest = useRef(0);
    const beginDetailRequest = () => ++detailRequest.current;
    const isCurrentDetailRequest = (token: number) => token === detailRequest.current;
    const [creating, setCreating] = useState(false);
    const [search, setSearch] = useState('');
    const [customerFilter, setCustomerFilter] = useState('');
    // Standaard alleen Open orders; geannuleerde orders blijven met reden en historie opvraagbaar.
    const [statusFilter, setStatusFilter] = useState<'Open' | 'Cancelled' | 'all'>('Open');

    useEffect(() => {
        const controller = new AbortController();
        setReference({ status: 'loading' });
        Promise.all([getCustomers(controller.signal), getProductsWithVariants(controller.signal)])
            .then(([customers, products]) => setReference({ status: 'ready', data: { customers, products } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setReference({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [referenceToken]);

    useEffect(() => {
        const controller = new AbortController();
        setOrders((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        setListRefreshing(true);
        listSalesOrders({}, controller.signal)
            .then((data) => { setOrders({ status: 'ready', data }); setListRefreshFailed(false); })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind === 'aborted') return;
                // Een eerder geladen lijst blijft zichtbaar; alleen zonder lijst wordt de foutweergave getoond.
                setOrders((current) => (current.status === 'ready' ? current : { status: 'error', error: apiError }));
                setListRefreshFailed(true);
            })
            .finally(() => { if (!controller.signal.aborted) setListRefreshing(false); });
        return () => controller.abort();
    }, [ordersToken]);

    useEffect(() => {
        const token = beginDetailRequest();
        setDetailRefreshing(false);
        setDetailRefreshFailed(false);
        if (selectedId === null) {
            setDetail(null);
            return;
        }
        const controller = new AbortController();
        setDetail({ status: 'loading' });
        getSalesOrder(selectedId, controller.signal)
            .then((order) => { if (isCurrentDetailRequest(token)) setDetail({ status: 'ready', data: order }); })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted' && isCurrentDetailRequest(token)) setDetail({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [selectedId]);

    const select = useCallback((id: number | null) => {
        const params = new URLSearchParams(searchParams.toString());
        if (id === null) params.delete('orderId');
        else params.set('orderId', String(id));
        const query = params.toString();
        router.replace(query === '' ? pathname : `${pathname}?${query}`);
    }, [router, pathname, searchParams]);

    const openOrder = (id: number) => {
        setCreating(false);
        select(id);
    };

    // Direct na een geslaagde aanmaak (ook bij een replay) staat de nieuwe order in de lijst, nog vóór de gebruiker kiest.
    const onOrderSaved = useCallback((order: SalesOrder) => {
        setOrders((current) => (current.status === 'ready' && !current.data.some((existing) => existing.id === order.id)
            ? { status: 'ready', data: [order, ...current.data] }
            : current));
        setOrdersToken((n) => n + 1);
    }, []);

    // Vanuit de lijst met onafgeronde commando's terug naar de plek waar de identieke opdracht wordt getoond.
    const goToCommand = (command: PendingCommand) => {
        if (command.kind === 'create-order') {
            setCreating(true);
        } else {
            openOrder(command.orderId);
        }
    };

    const createPending = commands[CREATE_ORDER_KEY] !== undefined;

    // Na een schrijfactie: de order opnieuw lezen (leidend voor reserveringen en on hold) en de lijst verversen.
    const reloadOrder = useCallback(async (): Promise<boolean> => {
        if (selectedId === null) return false;
        const token = beginDetailRequest();
        setOrdersToken((n) => n + 1);
        try {
            const order = await getSalesOrder(selectedId);
            if (!isCurrentDetailRequest(token)) return false; // intussen een andere order gekozen: antwoord negeren
            setDetail({ status: 'ready', data: order });
            return true;
        } catch (caught) {
            if (isCurrentDetailRequest(token)) setDetail({ status: 'error', error: toApiError(caught) });
            return false;
        }
    }, [selectedId]);

    // Knop "Vernieuwen" bij de geselecteerde order: leest de order en de lijst opnieuw, verstuurt niets en laat het huidige
    // detail (met open formulieren en bevroren commando's) staan tot de verse order er is. Mislukt het: melding, geen wissel.
    const refreshOrder = useCallback(async (): Promise<boolean> => {
        if (selectedId === null) return false;
        const token = beginDetailRequest();
        setDetailRefreshing(true);
        setDetailRefreshFailed(false);
        setOrdersToken((n) => n + 1);
        try {
            const order = await getSalesOrder(selectedId);
            if (!isCurrentDetailRequest(token)) return false; // verouderd antwoord: de gekozen order blijft staan
            setDetail({ status: 'ready', data: order });
            return true;
        } catch {
            if (isCurrentDetailRequest(token)) setDetailRefreshFailed(true);
            return false;
        } finally {
            if (isCurrentDetailRequest(token)) setDetailRefreshing(false);
        }
    }, [selectedId]);

    const visibleOrders = useMemo(() => {
        if (orders.status !== 'ready') return [];
        return orders.data
            .filter((order) => statusFilter === 'all' || order.status === statusFilter)
            .filter((order) => customerFilter === '' || String(order.customerId) === customerFilter)
            .filter((order) => matchesOrderSearch(order, search));
    }, [orders, customerFilter, search, statusFilter]);

    const customers = reference.status === 'ready' ? reference.data.customers : [];

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('sales')}</h1>
                    <p className="text-sm text-gray-600">{t('sales_subtitle')}</p>
                </div>
                <div className="w-64">
                    <label htmlFor="actorReference-page" className="text-xs font-medium text-gray-700">{t('supply_actor')} *</label>
                    <input id="actorReference-page" value={actor} maxLength={100} onChange={(event) => setActor(event.target.value)}
                        placeholder={t('supply_actor_placeholder')} className={inputClass} aria-describedby="actorReference-hint" />
                    <span id="actorReference-hint" className="block text-xs text-gray-500">{t('sales_actor_hint')}</span>
                </div>
            </div>

            {hydrated && <PendingCommandsBanner commands={commands} onGoTo={goToCommand} />}

            <p className="flex flex-wrap items-center gap-2 rounded border border-violet-300 bg-violet-50 px-3 py-2 text-sm text-violet-900">
                <span aria-hidden="true">🔒</span>{t('sales_direct_trade_note')}
                <Link href="/orders" className="ml-auto text-xs font-medium underline underline-offset-2">{t('sales_legacy_link')}</Link>
            </p>

            <div className="grid grid-cols-[30rem_minmax(0,1fr)] gap-4">
                <aside className="space-y-2">
                    <div className="flex items-center justify-between gap-2">
                        <h2 className="text-sm font-semibold text-gray-900">{t('sales_orders_title')}</h2>
                        <div className="flex items-center gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={() => setOrdersToken((n) => n + 1)} disabled={listRefreshing}
                                aria-busy={listRefreshing} aria-label={t('sales_refresh')} title={t('sales_refresh')}>
                                <span aria-hidden="true">↻</span>{listRefreshing && <span className="sr-only">{t('sales_refreshing')}</span>}
                            </button>
                            <button type="button" className={creating ? secondaryButtonClass : primaryButtonClass} onClick={() => setCreating(true)}
                                disabled={creating || reference.status !== 'ready' || !hydrated}>
                                {createPending ? t('sales_pending_open_create') : `+ ${t('sales_new_order')}`}
                            </button>
                        </div>
                    </div>
                    {listRefreshFailed && orders.status === 'ready' && (
                        <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-xs text-red-900">{t('sales_list_refresh_failed')}</p>
                    )}
                    <div className="grid grid-cols-3 gap-2">
                        <label className="text-xs font-medium text-gray-700">
                            {t('sales_col_status')}
                            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as 'Open' | 'Cancelled' | 'all')} className={inputClass}>
                                <option value="Open">{t('sales_status_open')}</option>
                                <option value="Cancelled">{t('sales_status_cancelled')}</option>
                                <option value="all">{t('sales_status_all')}</option>
                            </select>
                        </label>
                        <label className="text-xs font-medium text-gray-700">
                            {t('supply_receive_search')}
                            <input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t('sales_search_placeholder')} className={inputClass} />
                        </label>
                        <label className="text-xs font-medium text-gray-700">
                            {t('sales_customer')}
                            <select value={customerFilter} onChange={(event) => setCustomerFilter(event.target.value)} className={inputClass}>
                                <option value="">{t('sales_all_customers')}</option>
                                {customers.map((customer) => <option key={customer.id} value={customer.id}>{customer.name}</option>)}
                            </select>
                        </label>
                    </div>
                    {orders.status === 'loading' && <LoadingRows rows={5} label={t('sales_loading')} />}
                    {orders.status === 'error' && <ErrorState error={orders.error} onRetry={() => setOrdersToken((n) => n + 1)} />}
                    {orders.status === 'ready' && visibleOrders.length === 0 && (
                        <EmptyState title={orders.data.length === 0 ? t('sales_no_orders') : t('sales_no_matches')} description={orders.data.length === 0 ? t('sales_no_orders_hint') : undefined} />
                    )}
                    {orders.status === 'ready' && visibleOrders.length > 0 && (
                        <SalesOrderList orders={visibleOrders} selectedId={creating ? null : selectedId} onSelect={openOrder} />
                    )}
                    {orders.status === 'ready' && orders.data.length >= 200 && <p className="text-xs text-gray-600">{t('sales_list_capped')}</p>}
                </aside>

                <section aria-live="polite">
                    {creating && reference.status === 'loading' && <LoadingRows rows={4} label={t('supply_loading_reference')} />}
                    {creating && reference.status === 'error' && <ErrorState error={reference.error} onRetry={() => setReferenceToken((n) => n + 1)} />}
                    {creating && reference.status === 'ready' && (
                        <HydratedGate>
                            <CreateSalesOrderForm actor={actor} customers={reference.data.customers} products={reference.data.products}
                                onSaved={onOrderSaved} onOpen={openOrder} onCancel={() => setCreating(false)} />
                        </HydratedGate>
                    )}
                    {!creating && selectedId === null && (
                        <EmptyState title={t('sales_select_order')} description={t('sales_select_order_hint')} />
                    )}
                    {!creating && detail?.status === 'loading' && <LoadingRows rows={4} label={t('sales_loading_order')} />}
                    {!creating && detail?.status === 'error' && <ErrorState error={detail.error} onRetry={() => void reloadOrder()} />}
                    {!creating && detail?.status === 'ready' && (
                        <HydratedGate fallback={<LoadingRows rows={4} label={t('sales_loading_order')} />}>
                            <SalesOrderDetail order={detail.data} actor={actor} onOrderChanged={reloadOrder}
                                onRefresh={refreshOrder} refreshing={detailRefreshing} refreshFailed={detailRefreshFailed} />
                        </HydratedGate>
                    )}
                </section>
            </div>
        </div>
    );
}
