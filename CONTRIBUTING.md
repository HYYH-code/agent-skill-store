# Contributing

## Development Setup

Install .NET SDK `10.0.203`, Node.js `24`, and npm. Then run:

```bash
cd web && npm ci && npm run build && cd ..
dotnet restore AgentSkillStore.slnx
dotnet build AgentSkillStore.slnx -c Release --no-restore
```

## Pull Requests

- Create a focused branch from `main`.
- Keep changes scoped and preserve public API compatibility unless the change is explicitly documented.
- Add or update tests for changed behavior.
- Do not introduce legacy brand assets, real credentials, generated release archives, or local data files.
- Preserve existing copyright notices on derived files.
- Update documentation when commands, configuration, APIs, or security behavior change.

## Required Checks

```bash
cd web && npm test && npm run typecheck && npm run build && cd ..
dotnet build AgentSkillStore.slnx -c Release --no-restore
dotnet test AgentSkillStore.slnx -c Release --no-build
docker build -f docker/Dockerfile -t agent-skill-store:pr .
```

Use concise imperative commit messages. One logical change per commit is preferred.
