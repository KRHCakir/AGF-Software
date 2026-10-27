import { formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import type { PackageUnit } from '../../types/availability';

// Palletverhouding van een opgeslagen regel: "3 pallets × 120" of "3 pallets × 120 + 15 colli"; null bij gewone invoer.
export function palletBreakdownLabel(line: { palletCount: number | null; packagesPerPallet: number | null; loosePackages?: number | null }, unit: PackageUnit | null,
    t: (key: string, variables?: Record<string, string>) => string, locale: string): string | null {
    if (line.palletCount === null || line.packagesPerPallet === null) return null;
    const values = { pallets: formatQuantity(line.palletCount, locale), perPallet: formatQuantity(line.packagesPerPallet, locale) };
    return line.loosePackages
        ? t('pallet_breakdown_loose', { ...values, loose: formatQuantity(line.loosePackages, locale), unit: unit === null ? '' : t(unitLabelKey(unit)).toLowerCase() })
        : t('pallet_breakdown', values);
}
