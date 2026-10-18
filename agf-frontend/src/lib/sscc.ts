// Dezelfde GS1-controle als de backend (AgfAdmin/Services/Sscc.cs): precies 18 ASCII-cijfers met een geldig
// mod-10-controlecijfer. Er wordt niets genormaliseerd en deze applicatie genereert nooit een SSCC.

export function isValidSscc(value: string): boolean {
    if (!/^[0-9]{18}$/.test(value)) {
        return false;
    }
    let sum = 0;
    for (let i = 16; i >= 0; i--) {
        const weight = (16 - i) % 2 === 0 ? 3 : 1;
        sum += Number(value[i]) * weight;
    }
    const checkDigit = (10 - (sum % 10)) % 10;
    return Number(value[17]) === checkDigit;
}
