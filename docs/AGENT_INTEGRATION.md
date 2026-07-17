# Agent Integration

Agents integrate with Agent Skill Store through the `skillstore` CLI or the public discovery APIs.

## Recommended CLI Flow

1. Configure `AGENT_SKILL_STORE_URL`.
2. Configure an Agent Key only when publishing or accessing authenticated operations.
3. Run `skillstore list` or `skillstore search` to discover approved Skills.
4. Run `skillstore install <namespace>/<name>@<version> --target <agent>`.
5. Let the CLI verify the artifact digest and apply the local installation.

The registry does not push files into an Agent runtime and does not require a separate bridge service.

## API Discovery

- `/.well-known/agent-skills/index.json` for standards-based discovery.
- `/manifest.json` for linked version and sub-agent resources.
- `/api/v1/skills` for public search and metadata.

See [CLI.md](CLI.md) and [specs/README.md](specs/README.md) for complete contracts.
