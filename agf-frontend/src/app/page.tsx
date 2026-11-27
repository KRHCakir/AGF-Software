import { redirect } from 'next/navigation';

// Wie de software opent, komt direct op Start. Het oude keuzescherm (Desktop Admin of Mobile Sales) is weg; de mobiele verkoop komt
// later terug. redirect() werkt op de server (307), dus zonder flits van een tussenscherm, ook in de Docker-build van de demo.
export default function Home() {
    redirect('/dashboard');
}
