import type { CertificateOverviewItem, CertificateStatus, RelationCertificateSummary } from '../types/certificate';

// Weergave van certificaten in lijsten (certificaten deel 1): een korte naam voor de bekende soorten, de tekst bij een bijna verlopen
// certificaat (leverancier: controleren; klant: opvragen) en de badge per relatie. Geen React: met node:test te toetsen.

const SHORT: Record<string, string> = { GlobalGapIfa: 'GGN', GlobalGapCoc: 'CoC', Skal: 'SKAL', RainforestAlliance: 'RA', AmforiBsci: 'BSCI', Iso22000: 'ISO 22000', LeafMarque: 'LEAF' };

export function shortTypeName(code: string, typeName: string): string {
    return SHORT[code] ?? typeName;
}

// Wat te doen bij een certificaat dat bijna verloopt of verlopen is: bij een leverancier "Controleer of er een nieuw certificaat is",
// bij een klant "Vraag het nieuwe certificaat op bij [klant]", bij het eigen bedrijf vernieuwen. Een relatie die beide is: als leverancier.
export function attentionAction(item: Pick<CertificateOverviewItem, 'relationId' | 'relationName' | 'isCustomer' | 'isVendor'>): { key: string; params: Record<string, string> } {
    if (item.relationId === null) return { key: 'cert_action_own', params: {} };
    if (item.isVendor) return { key: 'cert_action_vendor', params: { name: item.relationName ?? '' } };
    return { key: 'cert_action_request', params: { name: item.relationName ?? '' } };
}

// De zwaarste status van een relatie voor de badge: verlopen gaat voor bijna verlopen; anders geen badge.
export function relationAttention(certificates: RelationCertificateSummary[] | undefined): CertificateStatus | null {
    if (!certificates) return null;
    if (certificates.some((c) => c.status === 'Expired')) return 'Expired';
    if (certificates.some((c) => c.status === 'ExpiringSoon')) return 'ExpiringSoon';
    return null;
}

// Link naar de relatie met het blok Certificaten open (of de instellingen voor het eigen bedrijf).
export function certificateLink(relationId: number | null): string {
    return relationId === null ? '/settings#certificaten' : `/relations?open=${relationId}&section=certificates`;
}
