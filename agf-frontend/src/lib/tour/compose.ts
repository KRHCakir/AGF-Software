import type { TourAction, TourStep } from './types';

// Een stap met de tekstsleutels tour_<prefix>_<id>_title, _what en _why.
export const step = (prefix: string, id: string, route: string, actions: readonly TourAction[]): TourStep => ({
    id,
    route,
    titleKey: `tour_${prefix}_${id}_title`,
    whatKey: `tour_${prefix}_${id}_what`,
    whyKey: `tour_${prefix}_${id}_why`,
    actions,
});

// Stappen van een ander hoofdstuk als voorwaarde: alleen als de vlag (needVendor, needOrder …) gezet is, anders overgeslagen. Eigen ids
// (prefix), zodat een hoofdstuk dezelfde stap van twee hoofdstukken kan bevatten; teksten en acties blijven precies die van het origineel.
export function prerequisite(steps: readonly TourStep[], prefix: string, when: string): TourStep[] {
    return steps.map((original) => ({ ...original, id: `${prefix}-${original.id}`, when }));
}
