FROM node:24-alpine AS web-build
WORKDIR /src
COPY . .
WORKDIR /src/web
RUN npm ci && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS server-build
WORKDIR /src
COPY --from=web-build /src .
RUN dotnet restore AgentSkillStore.slnx \
    && dotnet publish src/AgentSkillStore.Server/AgentSkillStore.Server.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The base image controls the Debian package snapshot used for curl.
# hadolint ignore=DL3008
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=server-build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AgentSkillStore.Server.dll"]
