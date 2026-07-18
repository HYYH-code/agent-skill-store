# Release Notes

## v0.2.0 - 2026-07-18

This release introduces one shared Skill installation model for Codex, Pi, OpenCode, and Claude.

### Included

- Versioned immutable packages under `.agents/.skillstore/packages`.
- Stable `.agents/skills/<name>` entries shared by Codex, Pi, and OpenCode.
- Strict Claude bridges through user or project `.claude/skills` directories.
- Windows Junction and Linux/macOS symlink activation with no copy fallback.
- User and project installation scopes with exact `skillstore.lock.json` restoration.
- `skillstore sync`, `doctor`, `bridge`, and `migrate` commands.
- Permission and digest validation during lockfile synchronization.
- Schema v1 direct-install compatibility and explicit migration.
- Transactional activation, rollback, bridge recovery, and per-scope operation locks.

### Distribution

The v0.2.0 workflow builds four CLI archives, NuGet packages, an OCI archive, and `SHA256SUMS` without publishing to NuGet.org or GHCR.
