# syntax=docker/dockerfile:1
# De website voor de demo-omgeving (deploy/docker-compose.demo.yml). Bouwen vanaf de hoofdmap van de repo:
#   docker build -f deploy/web.Dockerfile .
# NEXT_PUBLIC_* en API_INTERNAL_URL worden bij de build vastgelegd (next.config.ts): een andere waarde vraagt een nieuwe build.

FROM node:24-bookworm-slim AS build
WORKDIR /app
COPY agf-frontend/package.json agf-frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY agf-frontend/ ./
ARG API_INTERNAL_URL=http://api:8080
ARG NEXT_PUBLIC_DEMO_ENVIRONMENT=true
ARG NEXT_PUBLIC_DEMO_ASSISTANT=true
ARG NEXT_PUBLIC_DEMO_TOUR=true
ENV NEXT_TELEMETRY_DISABLED=1 \
    NEXT_OUTPUT=standalone \
    API_INTERNAL_URL=$API_INTERNAL_URL \
    NEXT_PUBLIC_DEMO_ENVIRONMENT=$NEXT_PUBLIC_DEMO_ENVIRONMENT \
    NEXT_PUBLIC_DEMO_ASSISTANT=$NEXT_PUBLIC_DEMO_ASSISTANT \
    NEXT_PUBLIC_DEMO_TOUR=$NEXT_PUBLIC_DEMO_TOUR
RUN npm run build

FROM node:24-bookworm-slim
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public
USER node
EXPOSE 3000
CMD ["node", "server.js"]
