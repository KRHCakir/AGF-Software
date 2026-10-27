// Gewicht in kg uit het invoerveld bij ontvangst. Gebruikers typen Nederlands: "15.000" of "1.250,5".
export function parseReceiptWeight(input: string): number | null {
  const trimmed = input.trim();
  if (trimmed === "") return null;
  const normalized = trimmed.replace(",", ".");
  const value = Number.parseFloat(normalized);
  if (!Number.isFinite(value) || value < 0) return null;
  return value;
}
