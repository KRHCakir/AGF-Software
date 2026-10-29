'use client';

import { useCallback, useEffect, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import DestinationChooser from '../../../components/supply/DestinationChooser';
import { inputClass } from '../../../components/supply/FieldError';
import PlanSupplyForm, { ReferenceData } from '../../../components/supply/PlanSupplyForm';
import ReceivePalletForm from '../../../components/supply/ReceivePalletForm';
import { useActorReference } from '../../../components/supply/useActorReference';
import { useTranslation } from '../../../i18n/LanguageContext';
import { SupplyShortcutMode, modeFromHash, subscribeSupplyMode } from '../../../lib/supplyShortcut';
import { toApiError } from '../../../services/apiClient';
import { getCustomers, getProductsWithVariants, getVendors } from '../../../services/referenceDataService';
import { SupplyRoute } from '../../../types/availability';
import { SupplyCommitment } from '../../../types/supply';

type Mode = 'plan' | 'receive';

// Kantoorwerkplek "Aanvoer": één werkplek met twee verbonden modi, alleen via bestaande API-contracten.
// Buiten scope: wijzigen, annuleren, statuswijzigingen, kwaliteit, splitsen, ongeplande ontvangst en DirectTrade-zendingen.
export default function SupplyPage() {
    const { t } = useTranslation();
    const [actor, setActor] = useActorReference();
    const [destination, setDestination] = useState<SupplyRoute | null>(null);
    const [mode, setMode] = useState<Mode>('plan');
    // Snelkoppelingen uit de zijbalk: bij aankomst via de hash (#plan of #receive), en terwijl de pagina open staat via de
    // gebeurtenis van de zijbalk of een hashwijziging. Altijd expliciet: "Aanvoer plannen" zet plannen, ook als ontvangen actief was.
    useEffect(() => {
        const apply = (shortcut: SupplyShortcutMode) => {
            setDestination('Warehouse');
            setMode(shortcut);
            setPreselected(null);
        };
        const initial = modeFromHash(window.location.hash);
        if (initial !== null) apply(initial);
        return subscribeSupplyMode(apply);
    }, []);
    const [reference, setReference] = useState<LoadState<ReferenceData>>({ status: 'loading' });
    const [referenceToken, setReferenceToken] = useState(0);
    const [preselected, setPreselected] = useState<SupplyCommitment | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        setReference({ status: 'loading' });
        Promise.all([getVendors(controller.signal), getCustomers(controller.signal), getProductsWithVariants(controller.signal)])
            .then(([vendors, customers, products]) => setReference({ status: 'ready', data: { vendors, customers, products } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') {
                    setReference({ status: 'error', error: apiError });
                }
            });
        return () => controller.abort();
    }, [referenceToken]);

    const chooseDestination = (route: SupplyRoute) => {
        setDestination(route);
        // Directe handel kent in deze PR alleen plannen; ontvangst van pallets is uitsluitend voor het eigen magazijn.
        if (route === 'DirectTrade') {
            setMode('plan');
        }
    };

    const receiveFor = (commitment: SupplyCommitment) => {
        setPreselected(commitment);
        setMode('receive');
    };

    const consumePreselected = useCallback(() => setPreselected(null), []);

    const modes: { value: Mode; label: string; hint: string }[] = destination === 'Warehouse'
        ? [
            { value: 'plan', label: t('supply_mode_plan'), hint: t('supply_mode_plan_hint') },
            { value: 'receive', label: t('supply_mode_receive'), hint: t('supply_mode_receive_hint') },
        ]
        : [{ value: 'plan', label: t('supply_mode_plan'), hint: t('supply_mode_plan_direct_hint') }];

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('supply')}</h1>
                    <p className="text-sm text-gray-600">{t('supply_subtitle')}</p>
                </div>
                <div className="w-64">
                    <label htmlFor="actorReference-page" className="text-xs font-medium text-gray-700">{t('supply_actor')}</label>
                    <input id="actorReference-page" value={actor} maxLength={100} onChange={(event) => setActor(event.target.value)}
                        placeholder={t('supply_actor_placeholder')} className={inputClass} aria-describedby="actorReference-hint" />
                    <span id="actorReference-hint" className="block text-xs text-gray-500">{t('supply_actor_hint')}</span>
                </div>
            </div>

            <DestinationChooser value={destination} onChange={chooseDestination} />

            {destination !== null && (
                <fieldset className="flex flex-wrap items-center gap-2">
                    <legend className="sr-only">{t('supply_mode_label')}</legend>
                    {modes.map((option) => (
                        <label key={option.value}
                            className="flex cursor-pointer items-start gap-2 rounded border border-gray-300 bg-white px-3 py-2 text-sm has-[:checked]:border-blue-500 has-[:checked]:bg-blue-50">
                            <input type="radio" name="supply-mode" value={option.value} checked={mode === option.value} data-tour="supply-mode" data-tour-key={option.value}
                                onChange={() => setMode(option.value)} className="mt-0.5" />
                            <span>
                                <span className="block font-medium text-gray-900">{option.label}</span>
                                <span className="block text-xs text-gray-600">{option.hint}</span>
                            </span>
                        </label>
                    ))}
                    {destination === 'DirectTrade' && (
                        <p className="text-xs text-violet-900">{t('supply_direct_receive_later')}</p>
                    )}
                </fieldset>
            )}

            {destination === null && <p className="text-sm text-gray-700">{t('supply_choose_destination_first')}</p>}

            {destination !== null && mode === 'plan' && (
                <>
                    {reference.status === 'loading' && <LoadingRows rows={4} label={t('supply_loading_reference')} />}
                    {reference.status === 'error' && <ErrorState error={reference.error} onRetry={() => setReferenceToken((n) => n + 1)} />}
                    {reference.status === 'ready' && (
                        <PlanSupplyForm key={destination} route={destination} reference={reference.data} onReceive={receiveFor} />
                    )}
                </>
            )}

            {destination === 'Warehouse' && mode === 'receive' && (
                <ReceivePalletForm actor={actor} preselected={preselected} onPreselectedConsumed={consumePreselected} />
            )}
        </div>
    );
}
