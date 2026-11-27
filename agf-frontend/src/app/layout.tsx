import './globals.css';
import type { Metadata } from 'next';
import DemoEnvironmentBar from '../components/demo/DemoEnvironmentBar';
import HistoryFirst from '../components/shared/HistoryFirst';
import { APP_NAME } from '../lib/appName';
import { demoEnvironmentEnabled, demoRobots } from '../lib/demoEnvironment';

export const metadata: Metadata = {
  title: APP_NAME,
  description: 'AI-Powered Invoice & Logistics',
  // Alleen in de demo-omgeving: zoekmachines nemen de pagina's niet op (ook X-Robots-Tag, next.config.ts).
  robots: demoRobots(demoEnvironmentEnabled()),
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="flex flex-col min-h-dvh bg-gray-50 text-sm font-sans antialiased text-gray-900">
        {/* Vóór de router van Next.js: Terug en Vooruit eerst langs de weggaansbeveiliging (fixes 5b). */}
        <HistoryFirst />
        <DemoEnvironmentBar />
        {children}
      </body>
    </html>
  );
}
