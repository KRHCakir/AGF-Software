'use client';

import { useEffect, useMemo, useState } from 'react';
import { getAvailabilityOverview } from '../../services/availabilityService';
import { PackageUnit } from '../../types/availability';
import { SalesBalance } from '../../types/salesSupply';

export type BalanceState = { status: 'loading' } | { status: 'ready'; balance: SalesBalance } | { status: 'error' };

const zero = (productVariantId: number, unit: PackageUnit): SalesBalance => ({
    productVariantId, unit, stock: 0, expected: 0, expectedNotReceived: 0, expectedInInspection: 0, sold: 0, soldReserved: 0, free: 0, freeStock: 0, shortageQuantity: 0, shortage: false,
});

// Stand per gekozen variant + eenheid uit de bestaande beschikbaarheid (één lezing per variant; opnieuw na `token`). Een variant zonder
// aanbod en zonder verkoop heeft overal 0. Alleen een hulp bij het invullen: de server controleert bij opslaan opnieuw.
export function useSalesBalances(pairs: { productVariantId: number; unit: PackageUnit }[], token = 0): (productVariantId: number, unit: PackageUnit) => BalanceState {
    const variantKey = useMemo(() => Array.from(new Set(pairs.map((pair) => pair.productVariantId))).sort((a, b) => a - b).join(','), [pairs]);
    const [byVariant, setByVariant] = useState<Record<number, { status: 'loading' } | { status: 'error' } | { status: 'ready'; rows: SalesBalance[] }>>({});

    useEffect(() => {
        const ids = variantKey === '' ? [] : variantKey.split(',').map(Number);
        const controller = new AbortController();
        for (const id of ids) {
            // Een al geladen stand blijft staan tijdens het opnieuw lezen.
            setByVariant((current) => (current[id]?.status === 'ready' ? current : { ...current, [id]: { status: 'loading' } }));
            getAvailabilityOverview({ productVariantId: id, route: 'Warehouse' }, controller.signal)
                .then((overview) => {
                    const rows = overview.variants.filter((v) => v.productVariantId === id).flatMap((v) => v.warehouseSupply)
                        .map((row) => row.sales).filter((sales): sales is SalesBalance => sales !== null && sales !== undefined);
                    setByVariant((current) => ({ ...current, [id]: { status: 'ready', rows } }));
                })
                .catch(() => { if (!controller.signal.aborted) setByVariant((current) => ({ ...current, [id]: { status: 'error' } })); });
        }
        return () => controller.abort();
    }, [variantKey, token]);

    return (productVariantId, unit) => {
        const state = byVariant[productVariantId];
        if (!state || state.status === 'loading') return { status: 'loading' };
        if (state.status === 'error') return { status: 'error' };
        return { status: 'ready', balance: state.rows.find((row) => row.unit === unit) ?? zero(productVariantId, unit) };
    };
}
