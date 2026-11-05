// Schone start: welke stamgegevens een formulier nog mist (een inkooporder zonder leverancier in het systeem) en wanneer de kleine
// Beginnen-kaart op de startpagina staat. Geen imports: met node:test te toetsen.

export type MasterDataNeed = 'vendor' | 'customer' | 'article';

export interface MasterDataInput {
    vendors?: readonly { isActive: boolean }[];
    customers?: readonly { isActive: boolean }[];
    products?: readonly { variants: readonly { isArchived?: boolean }[] }[];
}

// Waar je het ontbrekende aanmaakt: de relatie- en artikelwerkplek openen meteen een nieuw formulier met ?new=1.
export const MASTER_DATA_HREF: Readonly<Record<MasterDataNeed, string>> = {
    vendor: '/relations?new=1',
    customer: '/relations?new=1',
    article: '/articles?new=1',
};

// Alleen actieve relaties en niet-gearchiveerde artikelen tellen: die kun je op een nieuw document kiezen. Een lijst die (nog) niet
// geladen is, telt niet als ontbrekend.
export function missingMasterData(input: MasterDataInput, needs: readonly MasterDataNeed[]): MasterDataNeed[] {
    const has: Record<MasterDataNeed, boolean | null> = {
        vendor: input.vendors === undefined ? null : input.vendors.some((vendor) => vendor.isActive),
        customer: input.customers === undefined ? null : input.customers.some((customer) => customer.isActive),
        article: input.products === undefined ? null : input.products.some((product) => product.variants.some((variant) => !variant.isArchived)),
    };
    return needs.filter((need) => has[need] === false);
}

export interface GettingStartedStep {
    id: 'relations' | 'articles' | 'order';
    href: string;
    done: boolean;
}

// De Beginnen-kaart staat er zolang er nog geen relaties of geen artikelen zijn, en verdwijnt vanzelf zodra beide er zijn.
// Volgorde: relaties (leveranciers en klanten), artikelen, dan de eerste inkooporder.
export function gettingStarted(counts: { relations: number; articles: number; orders: number }): { show: boolean; steps: GettingStartedStep[] } {
    const steps: GettingStartedStep[] = [
        { id: 'relations', href: MASTER_DATA_HREF.vendor, done: counts.relations > 0 },
        { id: 'articles', href: MASTER_DATA_HREF.article, done: counts.articles > 0 },
        { id: 'order', href: '/purchase-orders/new', done: counts.orders > 0 },
    ];
    return { show: counts.relations === 0 || counts.articles === 0, steps };
}
