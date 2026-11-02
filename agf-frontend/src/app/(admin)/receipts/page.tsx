'use client';

import { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import SupplyWorkspace from '../../../components/supply/SupplyWorkspace';

// Ontvangst: pallets registreren tegen een inkooporder (vroeger "Pallet ontvangen" op /supply#receive). ?order=<id> kiest die order alvast.
function ReceiptsContent() {
    const order = useSearchParams().get('order');
    // Alleen een geldig id (positief, binnen een 32-bits getal); anders geen voorkeuze.
    const orderId = order !== null && /^[1-9][0-9]{0,9}$/.test(order) && Number(order) <= 2147483647 ? Number(order) : null;
    return <SupplyWorkspace task="receipt" orderId={orderId} />;
}

// useSearchParams vraagt in Next.js 15 een Suspense-grens.
export default function ReceiptsPage() {
    return (
        <Suspense fallback={null}>
            <ReceiptsContent />
        </Suspense>
    );
}
