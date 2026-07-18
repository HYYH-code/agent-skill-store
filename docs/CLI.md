# skillstore CLI

`skillstore` is the local client for Agent Skill Store. It discovers and verifies remote artifacts, then performs installation changes on the local machine.

## Configuration

Priority order:

1. Command-line flags.
2. `AGENT_SKILL_STORE_URL` and `AGENT_SKILL_STORE_API_KEY`.
3. `~/.agent-skill-store/config.json`.

```bash
skillstore login --server https://skills.example.com --api-key '<key>'
skillstore config set server-url https://skills.example.com
skillstore config set api-key '<key>'
```

The configuration file is restricted to the current user on Unix-like systems. Prefer environment variables or a secret manager for automation.

## Bootstrap Key

Generate the first management credential offline:

```bash
skillstore api-key generate
```

Set the output as `AGENTSKILLSTORE__BOOTSTRAPAPIKEY` before the server's first startup. This command does not require a server URL or existing credential.

## Agent Keys

Creating, listing, and revoking keys requires `keys:manage`:

```bash
skillstore api-key create --label 'Build Agent'
skillstore api-key create --label 'Automation' --scope skills:read --scope skills:submit
skillstore api-key list
skillstore api-key delete 7
```

The raw key is printed once. List responses never include it.

## Discovery

```bash
skillstore list
skillstore search review
skillstore info code-review-checklist
skillstore versions code-review-checklist
```

Discovery is public by default and requires only a configured server URL.

## Local Installation

The CLI keeps one versioned local package and exposes one active entry per Skill:

| Path | Purpose |
| --- | --- |
| `~/.agents/.skillstore/packages/<name>/<version>/<digest>` | Immutable user package |
| `~/.agents/skills/<name>` | Current shared entry for Codex, Pi, and OpenCode |
| `~/.claude/skills/<name>` | Junction to the shared entry when Claude is detected |

```bash
skillstore install engineering/code-review-checklist@2.1.0
skillstore update engineering/code-review-checklist
skillstore rollback engineering/code-review-checklist
skillstore uninstall engineering/code-review-checklist
skillstore list installed
skillstore doctor
skillstore sync
```

Installation verifies the advertised SHA-256 digest before activating an immutable package. Windows uses Junctions and Unix-like systems use symlinks. Link creation failure is fatal; the previous active entry remains in place. Codex, Pi, and OpenCode discover `.agents/skills` natively, so installing there makes a Skill visible to all three clients.

### Project scope

Project installs use the nearest Git root and write an exact lockfile:

```bash
skillstore install engineering/code-review-checklist@2.1.0 --scope project
skillstore sync --scope project
```

This creates `skillstore.lock.json`. Generated package and bridge paths are added to local Git exclude files, while the lockfile remains suitable for source control. A project Skill that conflicts with a different user-level version is rejected rather than allowing Agent-specific version drift.

### Bridges and migration

Claude is the first non-native bridge target:

```bash
skillstore bridge add engineering/code-review-checklist --target claude
skillstore bridge remove engineering/code-review-checklist --target claude
skillstore bridge list
```

Inspect old direct installations before changing them:

```bash
skillstore migrate --dry-run
skillstore migrate --apply
```

Migration only moves unchanged installations already managed by `skillstore`; unmanaged directories and conflicts are reported and never overwritten.

## Governed Publishing

```bash
skillstore lint ./my-skill
skillstore submit ./my-skill.zip \
  --name my-skill \
  --display-name 'My Skill' \
  --namespace engineering \
  --version 1.0.0 \
  --owner 'Developer Experience' \
  --risk-level medium \
  --network-policy deny-all \
  --changelog 'Initial release'
skillstore submissions
```

`submit` creates a pending publication. Approval is performed by a separate reviewer in the web console or governance API.

## Legacy Direct Publishing

The `publish`, `publish-all`, `delete`, `publish-subagent`, and `delete-subagent` write paths are retained for migration scenarios. The server rejects direct Skill publishing unless `AgentSkillStore:Compatibility:AllowDirectPublish` is explicitly enabled.

## Global Options

| Option | Purpose |
| --- | --- |
| `--server-url <url>` | Override the configured server |
| `--api-key <key>` | Override the configured Agent Key |
| `--output text\|json` | Select output format |
| `--target all\|codex\|claude\|pi\|opencode\|agents` | Select bridge behavior; `.agents` remains shared |
| `--scope user\|project` | Select user or project installation scope |
| `--install-root <path>` | Override the `.agents` root |
| `--allow-non-stable` | Permit explicit beta or deprecated versions |
| `--yes` | Confirm destructive changes or permission expansion |
| `--verbose` | Enable request diagnostics without logging credentials |
