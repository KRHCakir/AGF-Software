// De demo-omgeving online (docs/demo-online.md): een vaste build met NEXT_PUBLIC_DEMO_ENVIRONMENT=true. Dan staat op elk scherm de
// demo-balk en vragen pagina's zoekmachines ze niet op te nemen. De backend dwingt de rest af (AGF_DEMO_ENVIRONMENT=true).
export const DEMO_BAR_TITLE = 'Demo-omgeving';
export const DEMO_BAR_TEXT = 'gebruik alleen verzonnen gegevens. Gegevens kunnen worden teruggezet.';

// Letterlijke verwijzing, zodat Next.js de waarde bij de build kan invullen.
export function demoEnvironmentEnabled(value: string | undefined = process.env.NEXT_PUBLIC_DEMO_ENVIRONMENT): boolean {
    return value === 'true';
}

// Voor de metadata van de pagina's (src/app/layout.tsx); naast de kop X-Robots-Tag uit next.config.ts.
export function demoRobots(enabled: boolean): { index: false; follow: false } | undefined {
    return enabled ? { index: false, follow: false } : undefined;
}

// De nagebootste afzender (tests, rondleiding en de demo-omgeving) geeft elk bericht een id op dit domein: zo'n mail is niet echt verstuurd.
export const SIMULATED_MESSAGE_ID_SUFFIX = '@nagebootst.agf.local>';

export function isSimulatedSend(messageId: string | null | undefined): boolean {
    return typeof messageId === 'string' && messageId.endsWith(SIMULATED_MESSAGE_ID_SUFFIX);
}
