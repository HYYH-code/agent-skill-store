# Tooling

## Required Versions

| Tool | Version |
| --- | --- |
| .NET SDK | `10.0.203` |
| Node.js | `24` |
| npm | bundled with Node.js 24 |
| Docker | current stable, optional for container checks |

`global.json` pins the .NET SDK used by CI and local builds.

## Frontend

```bash
cd web
npm ci
npm test
npm run typecheck
npm run build
```

The production build is written to `src/AgentSkillStore.Server/wwwroot`.

## .NET

```bash
dotnet restore AgentSkillStore.slnx
dotnet build AgentSkillStore.slnx -c Release --no-restore
dotnet test tests/AgentSkillStore.Tests/AgentSkillStore.Tests.csproj -c Release --no-build
dotnet test tests/AgentSkillStore.Integration.Tests/AgentSkillStore.Integration.Tests.csproj -c Release --no-build
dotnet test tests/AgentSkillStore.Cli.Tests/AgentSkillStore.Cli.Tests.csproj -c Release --no-build
```

Run E2E tests after installing Playwright Chromium:

```bash
dotnet build tests/AgentSkillStore.E2E.Tests/AgentSkillStore.E2E.Tests.csproj -c Release
pwsh tests/AgentSkillStore.E2E.Tests/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test tests/AgentSkillStore.E2E.Tests/AgentSkillStore.E2E.Tests.csproj -c Release --no-build
```

## Local Server

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://0.0.0.0:8081
export AGENTSKILLSTORE__DATAPATH="$PWD/data"
export AGENTSKILLSTORE__BASEURL=http://127.0.0.1:8081
dotnet run --project src/AgentSkillStore.Server -c Release
```

## Packages

```bash
dotnet pack src/AgentSkillStore.Client/AgentSkillStore.Client.csproj -c Release -o artifacts/nuget
dotnet pack src/AgentSkillStore.Cli/AgentSkillStore.Cli.csproj -c Release -o artifacts/nuget
```

## CLI Platforms

```bash
dotnet publish src/AgentSkillStore.Cli/AgentSkillStore.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/cli/linux-x64
dotnet publish src/AgentSkillStore.Cli/AgentSkillStore.Cli.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -o artifacts/cli/linux-arm64
dotnet publish src/AgentSkillStore.Cli/AgentSkillStore.Cli.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o artifacts/cli/osx-arm64
dotnet publish src/AgentSkillStore.Cli/AgentSkillStore.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/cli/win-x64
```

## Container

```bash
docker build -f docker/Dockerfile -t agent-skill-store:0.2.0 .
docker compose -f docker/docker-compose.yml config
```

## Repository Scans

CI scans tracked source and built assets for removed brand terms, credential patterns, internal document formats, generated release archives, and stale compiled frontend bundles.
