// Vaste voorbeeldmeldingen en oude kengetallen zijn standaard verborgen; alleen met deze build-vlag zichtbaar.
export const showLegacyDemoChrome = process.env.NEXT_PUBLIC_SHOW_LEGACY_DEMO_CHROME === 'true';

// Demo-assistent (vaste rondleiding en orderzoeken, alleen-lezen) is standaard uit; aan met NEXT_PUBLIC_DEMO_ASSISTANT=true.
export const showDemoAssistant = process.env.NEXT_PUBLIC_DEMO_ASSISTANT === 'true';

// Autopilot van de rondleiding (zelf invullen en klikken) alleen op een demo-omgeving: NEXT_PUBLIC_DEMO_TOUR=true.
// Standaard uit; zonder deze vlag blijft de rondleiding alleen navigeren en uitleggen.
export const showDemoTourAutopilot = process.env.NEXT_PUBLIC_DEMO_TOUR === 'true';
