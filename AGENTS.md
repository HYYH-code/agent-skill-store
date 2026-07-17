# Agent Skill Store Contributor Guide

## Scope

Agent Skill Store is a self-hosted registry and governance service for AI agent skills. Keep changes aligned with these product boundaries:

- The server owns discovery, upload validation, review, publishing, versioning, audit, and artifact storage.
- The CLI owns local installation, update, uninstall, and rollback.
- The server does not execute Skills or write into a user's Agent directories.

## Repository Map

- `src/AgentSkillStore.Server`: ASP.NET Core server and static web application.
- `src/AgentSkillStore.Client`: typed .NET client.
- `src/AgentSkillStore.Cli`: `skillstore` CLI.
- `src/AgentSkillStore.AppHost`: Aspire development host.
- `web`: React and TypeScript source.
- `tests`: unit, integration, CLI, and E2E tests.

## Engineering Rules

- Target .NET SDK `10.0.203`, Node.js `24`, and npm.
- Keep JSON serialization source-generated and AOT-compatible.
- Preserve existing Petabridge copyright notices on derived source files.
- Do not log or persist raw API Keys.
- Keep public read endpoints separate from authenticated publishing and governance endpoints.
- Do not add compatibility aliases or legacy brand names.
- Add tests proportional to the behavior and security impact of a change.

## Verification

```bash
cd web && npm ci && npm test && npm run typecheck && npm run build && cd ..
dotnet restore AgentSkillStore.slnx
dotnet build AgentSkillStore.slnx -c Release --no-restore
dotnet test AgentSkillStore.slnx -c Release --no-build
```

Run targeted tests while implementing, then run the full suite before submitting a pull request.
