'use client';

import SupplyWorkspace from '../../../../components/supply/SupplyWorkspace';

// Inkooporder aanmaken (eigen magazijn): vroeger "Aanvoer plannen" op /supply#plan.
export default function PurchaseOrderNewPage() {
    return <SupplyWorkspace task="purchase-order" />;
}
