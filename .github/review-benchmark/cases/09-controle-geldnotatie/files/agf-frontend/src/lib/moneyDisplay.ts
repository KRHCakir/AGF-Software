// Bedrag voor op het scherm: altijd 2 decimalen, in de notatie van de schermtaal ("1.234,50" in het Nederlands).
const formatters = new Map<string, Intl.NumberFormat>();

export function formatMoneyDisplay(value: number, locale: string = "nl-NL"): string {
  if (!Number.isFinite(value)) return "";
  let formatter = formatters.get(locale);
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    formatters.set(locale, formatter);
  }
  return formatter.format(value);
}
