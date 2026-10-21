// Vaste voorbeeldmeldingen en oude kengetallen zijn standaard verborgen; alleen met deze build-vlag zichtbaar.
export const showLegacyDemoChrome = process.env.NEXT_PUBLIC_SHOW_LEGACY_DEMO_CHROME === 'true';

// Demo-assistent (vaste rondleiding en orderzoeken, alleen-lezen) is standaard uit; aan met NEXT_PUBLIC_DEMO_ASSISTANT=true.
export const showDemoAssistant = process.env.NEXT_PUBLIC_DEMO_ASSISTANT === 'true';
