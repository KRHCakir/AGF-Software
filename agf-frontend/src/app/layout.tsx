import './globals.css';
import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'AGF ERP System',
  description: 'AI-Powered Invoice & Logistics',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="flex flex-col min-h-dvh bg-gray-50 text-sm font-sans antialiased text-gray-900">
        {children}
      </body>
    </html>
  );
}
