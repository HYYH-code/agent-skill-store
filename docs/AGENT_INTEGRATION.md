# Agent Integration

Agents integrate with Agent Skill Store through the `skillstore` CLI or the public discovery APIs.

## Recommended CLI Flow

1. Configure `AGENT_SKILL_STORE_URL`.
2. Configure an Agent Key only when publishing or accessing authenticated operations.
3. Run `skillstore list` or `skillstore search` to discover approved Skills.
4. Run `skillstore install <namespace>/<name>@<version>`.
5. Let the CLI verify the artifact digest and expose the package through `.agents/skills`.

The CLI uses `.agents/skills` as the shared discovery root. Codex, Pi, and OpenCode read this location natively. Claude is bridged through a per-Skill link under `.claude/skills`; use `skillstore doctor` to inspect the resulting local state.

For repository-specific Skills, commit `skillstore.lock.json` and let other machines run:

```bash
skillstore sync --scope project
```

The registry does not push files into an Agent runtime and does not require a separate bridge service.

## API Discovery

- `/.well-known/agent-skills/index.json` for standards-based discovery.
- `/manifest.json` for linked version and sub-agent resources.
- `/api/v1/skills` for public search and metadata.

See [CLI.md](CLI.md) and [specs/README.md](specs/README.md) for complete contracts.
