import { formatKgOneDecimal } from '../../lib/quantityKg';

type Translate = (key: string, variables?: Record<string, string>) => string;

// Kg van een opgeslagen order- of aanvoerregel achter het aantal: "15.600,0 kg netto · 16.068,0 kg bruto"; null zonder kg.
// Met `withSource` ook "(berekend)" of "(ingevuld)", zodat een berekend gewicht nooit als opgegeven gewicht leest.
export function lineKgLabel(line: { netWeightKg?: number | null; grossWeightKg?: number | null; weightSource?: string | null }, t: Translate, locale: string, withSource = false): string | null {
    const parts = [
        line.netWeightKg != null ? t('quantity_kg_net', { net: formatKgOneDecimal(line.netWeightKg, locale) }) : null,
        line.grossWeightKg != null ? t('quantity_kg_gross', { gross: formatKgOneDecimal(line.grossWeightKg, locale) }) : null,
    ].filter((part): part is string => part !== null);
    if (parts.length === 0) return null;
    const source = !withSource ? '' : line.weightSource === 'Calculated' ? ` (${t('line_kg_calculated')})` : line.weightSource === 'Entered' ? ` (${t('line_kg_entered')})` : '';
    return `${parts.join(' · ')}${source}`;
}
