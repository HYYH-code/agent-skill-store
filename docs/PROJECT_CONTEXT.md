# Project Context

## Purpose

Agent Skill Store is a self-hosted distribution and governance system for AI agent skills. It combines a public catalog with authenticated publishing, independent review, versioned artifacts, audit, and a local installation CLI.

## Product Boundaries

- The web application and server manage catalog discovery, uploads, review, versions, teams, categories, API Keys, and audit.
- `skillstore` changes local Agent Skill directories only when invoked on the user's machine.
- The server stores and distributes artifacts but does not execute them.
- Public discovery includes only approved, installable versions.
- Direct unaudited publishing is disabled by default.

## Architecture

```text
Browser / API clients / skillstore CLI
                 |
        ASP.NET Core Minimal APIs
                 |
      Governance and artifact services
          |                    |
     SQLite metadata      SHA-256 blobs
```

## Projects

| Path | Purpose |
| --- | --- |
| `src/AgentSkillStore.Server` | HTTP APIs, governance workflows, persistence, static web hosting |
| `src/AgentSkillStore.Client` | Typed .NET client and verified artifact helpers |
| `src/AgentSkillStore.Cli` | Local CLI and Agent target adapters |
| `src/AgentSkillStore.AppHost` | Aspire development orchestration |
| `web` | React, TypeScript, Ant Design frontend |
| `tests/AgentSkillStore.Tests` | Unit tests |
| `tests/AgentSkillStore.Integration.Tests` | HTTP and workflow integration tests |
| `tests/AgentSkillStore.Cli.Tests` | CLI parsing and behavior tests |
| `tests/AgentSkillStore.E2E.Tests` | Aspire and Playwright browser tests |

## Security Invariants

- Raw API Keys are returned once and never logged or listed.
- The server stores SHA-256 hashes of API Keys.
- The first management Key must be imported from `AGENTSKILLSTORE__BOOTSTRAPAPIKEY`.
- Anonymous first-key creation is not supported.
- The last unexpired `keys:manage` Key cannot be revoked.
- Authors and reviewers are separate roles; a submitter cannot approve the same publication.
- Artifact digests are verified before local installation.
- Archive extraction must reject traversal and unsafe links.

## Standards

- AgentSkills.io-compatible `SKILL.md` packages.
- Cloudflare Agent Skills discovery index at `/.well-known/agent-skills/index.json`.
- Native linked manifest at `/manifest.json` for version and sub-agent aware clients.

## Runtime

- .NET 10 and ASP.NET Core Minimal APIs.
- SQLite and Dapper.
- Source-generated `System.Text.Json` contexts.
- React 19, TypeScript, Vite, and Ant Design.
- Content-addressable file storage using SHA-256.
