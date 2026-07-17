# AgentSkillStore.Cli

The `AgentSkillStore.Cli` .NET tool installs the `skillstore` command.

```bash
dotnet tool install --global AgentSkillStore.Cli
skillstore --help
```

Configure a registry:

```bash
skillstore login --server https://skills.example.com --api-key '<key>'
```

Discover and install Skills:

```bash
skillstore list
skillstore search review
skillstore install engineering/code-review-checklist@2.1.0 --target codex
skillstore update engineering/code-review-checklist --target codex
skillstore uninstall engineering/code-review-checklist --target codex
```

Generate the first server management credential offline:

```bash
skillstore api-key generate
```

The CLI uses `AGENT_SKILL_STORE_URL`, `AGENT_SKILL_STORE_API_KEY`, and `~/.agent-skill-store/config.json`.

See the repository [CLI documentation](../../docs/CLI.md) for publishing, API Key management, supported Agent targets, and security behavior.
