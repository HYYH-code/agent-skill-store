# Release Notes

## v0.1.0 - 2026-07-17

Initial open-source release of Agent Skill Store.

### Included

- React and TypeScript web catalog and governance console.
- Public Skill discovery, version history, native manifest, and verified artifacts.
- Governed package upload, validation, independent review, approval, and audit.
- Local Development accounts for administrator, author, reviewer, and engineer roles.
- `skillstore` CLI for local install, update, uninstall, rollback, publishing, and API Key management.
- Offline Bootstrap Key generation with SHA-256-only server storage.
- Protection against anonymous first-key creation and revoking the last valid management key.
- Typed `AgentSkillStore.Client` .NET package.
- SQLite metadata and SHA-256 content-addressed blob storage.
- Docker and Compose deployment assets.
- Four-platform CLI release archives, NuGet packages, OCI archives, and checksums.

### Distribution

The v0.1.0 workflow builds release artifacts without publishing to NuGet.org or GHCR.
