import type { DeliveryConfirmation } from '../types/salesOrder';

// De enige definitie van een actieve afleverbevestiging in de frontend, gelijk aan de backend (DeliveryState.IsActive): een bevestiging
// zonder intrekking. Badges, knoppen, panelen en de vormcontrole van API-antwoorden gebruiken alleen deze functies.
export function isActiveConfirmation(confirmation: DeliveryConfirmation): boolean {
    return confirmation.retraction === null;
}

export function activeConfirmation(history: DeliveryConfirmation[]): DeliveryConfirmation | null {
    const active = history.filter(isActiveConfirmation);
    return active.length === 1 ? active[0] : null;
}

// Een verzend-antwoord is alleen geldig als `delivery` exact de enige actieve bevestiging uit de historie is (of null zonder actieve),
// de historie oplopend genummerd is en hooguit één actieve bevat. Zo kan een historische bevestiging nooit als actief worden getoond.
export function hasConsistentDeliveryState(delivery: DeliveryConfirmation | null, history: DeliveryConfirmation[]): boolean {
    const active = history.filter(isActiveConfirmation);
    if (active.length > 1) return false;
    if (!history.every((confirmation, index) => confirmation.sequence === index + 1)) return false;
    if (delivery === null) return active.length === 0;
    return active.length === 1 && active[0].id === delivery.id && isActiveConfirmation(delivery);
}
