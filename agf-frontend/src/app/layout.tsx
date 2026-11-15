import './globals.css';
import type { Metadata } from 'next';
import DemoEnvironmentBar from '../components/demo/DemoEnvironmentBar';
import { demoEnvironmentEnabled, demoRobots } from '../lib/demoEnvironment';

export const metadata: Metadata = {
  title: 'AGF ERP System',
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
        <DemoEnvironmentBar />
        {children}
      </body>
    </html>
  );
}
