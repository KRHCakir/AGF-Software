// Certificaten deel 2: het GGN van het product op een inkoopregel. Het systeem stelt een nummer voor uit de geldige GGN-certificaten van de
// leverancier waarin het product staat: één treffer wordt voorgevuld, meer treffers vragen een keuze, geen treffer blijft leeg. Zolang de
// gebruiker het veld niet zelf aanraakt, volgt het het voorstel (ook na een ander product of een andere leverancier); daarna blijft het
// staan zoals de gebruiker het zette, ook leeg. Een handelaar factureert het GGN van zijn teler, dat niet in zijn eigen certificaat staat:
// daarom altijd te overschrijven. Pure module zonder React (node:test); imports met .ts-extensie.

import type { GgnProposal } from '../types/productGgn';
import { isGgn, normalizeGgn } from './ggn.ts';
import { hasValidGgnCheckDigit } from './certificateRules.ts';

export interface GgnLine {
    key: string;
    productVariantId: string;
    ggn: string;
    // De gebruiker zette het GGN zelf (typen, kiezen, leegmaken) of het kwam van een opgeslagen regel: het voorstel raakt het niet meer.
    ggnTouched?: boolean;
}

// Wat een niet-aangeraakte regel bij dit voorstel krijgt: het ene nummer, anders leeg.
export function proposedValue(proposal: GgnProposal | undefined): string {
    return proposal?.kind === 'One' ? proposal.options[0]?.ggn ?? '' : '';
}

// Zet het voorstel op de regels die de gebruiker niet zelf aanraakte. Geeft dezelfde lijst terug als er niets verandert (geen render-lus).
export function applyGgnProposals<T extends GgnLine>(lines: readonly T[], proposalOf: (line: T) => GgnProposal | undefined): readonly T[] {
    let changed = false;
    const next = lines.map((line) => {
        if (line.ggnTouched) return line;
        const value = proposedValue(proposalOf(line));
        if (line.ggn === value) return line;
        changed = true;
        return { ...line, ggn: value };
    });
    return changed ? next : lines;
}

// De toelichting onder het veld: voorgesteld, kiezen, eigen nummer (niet in het certificaat: bij een handelaar het GGN van de teler), of
// geen GGN in het certificaat. Null = niets te zeggen (nog geen product of nog geen voorstel geladen).
export type GgnHint = 'proposed' | 'choose' | 'chosen' | 'own' | 'none';
export function ggnHint(line: GgnLine, proposal: GgnProposal | undefined): GgnHint | null {
    if (line.productVariantId === '' || proposal === undefined) return null;
    const value = normalizeGgn(line.ggn);
    const known = proposal.options.some((option) => option.ggn === value);
    if (value === '') return proposal.kind === 'Many' ? 'choose' : proposal.kind === 'None' ? 'none' : null;
    if (known) return proposal.kind === 'One' && !line.ggnTouched ? 'proposed' : 'chosen';
    return 'own';
}

// Fout (geen 13 cijfers: blokkeert opslaan, de server weigert het ook) of waarschuwing (controlecijfer klopt niet: geen blokkade).
export function ggnProblem(raw: string): 'ggn_13_digits' | 'ggn_check_digit' | null {
    const value = normalizeGgn(raw);
    if (value === '') return null;
    if (!isGgn(value)) return 'ggn_13_digits';
    return hasValidGgnCheckDigit(value) ? null : 'ggn_check_digit';
}
