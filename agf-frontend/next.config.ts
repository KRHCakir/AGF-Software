import type { NextConfig } from "next";

// De browser roept de API aan op de eigen website (/api, zie src/services/apiConfig.ts); de website stuurt dat door naar de backend.
// API_INTERNAL_URL is het adres van de backend vanaf de webserver: lokaal standaard de API op poort 5197, in de demo-container
// http://api:8080. Next.js legt dit vast bij `next build`, dus voor een container is het een build-instelling (zie deploy/).
const apiInternalUrl = (process.env.API_INTERNAL_URL ?? "http://localhost:5197").trim().replace(/\/+$/, "");

const nextConfig: NextConfig = {
  // Alleen voor de container: een zelfstandige server zonder node_modules (NEXT_OUTPUT=standalone in de Dockerfile).
  output: process.env.NEXT_OUTPUT === "standalone" ? "standalone" : undefined,
  experimental: {
    // Het doorsturen naar de API breekt standaard af na 30 seconden. AI-lezen duurt tot 90 seconden en een mail versturen tot
    // 2 minuten (DocumentMailRules.SendTimeout); daarom ruim daarboven.
    proxyTimeout: 180_000,
  },
  // Demo-omgeving (NEXT_PUBLIC_DEMO_ENVIRONMENT=true, bij de build): zoekmachines nemen niets op. robots.txt weigert altijd alles.
  async headers() {
    return process.env.NEXT_PUBLIC_DEMO_ENVIRONMENT === "true"
      ? [{ source: "/:path*", headers: [{ key: "X-Robots-Tag", value: "noindex, nofollow" }] }]
      : [];
  },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiInternalUrl}/api/:path*` }];
  },
};

export default nextConfig;
