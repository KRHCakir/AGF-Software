# syntax=docker/dockerfile:1
# De API voor de demo-omgeving (deploy/docker-compose.demo.yml). Bouwen vanaf de hoofdmap van de repo:
#   docker build -f deploy/api.Dockerfile .
# De images volgen de .NET 9-lijn (9.0): bij elke nieuwe build komen de beveiligingsupdates mee.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY AgfDocumentAi/AgfDocumentAi.csproj AgfDocumentAi/
COPY AgfAdmin/AgfAdmin.csproj AgfAdmin/
RUN dotnet restore AgfAdmin/AgfAdmin.csproj
COPY AgfDocumentAi/ AgfDocumentAi/
COPY AgfAdmin/ AgfAdmin/
RUN dotnet publish AgfAdmin/AgfAdmin.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
# curl voor de gezondheidscontrole, jq en curl voor het vulscript (scripts/demo-seed.sh draait in deze container).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl jq \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY scripts/demo-seed.sh /app/scripts/demo-seed.sh
# Documenten (AI-upload, bijlagen) in een volume; de gebruiker app (geen root) moet erin kunnen schrijven.
RUN mkdir -p /data/documents && chown app:app /data/documents && chmod 0755 /app/scripts/demo-seed.sh
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DocumentAi__StoragePath=/data/documents \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "AgfAdmin.dll"]
