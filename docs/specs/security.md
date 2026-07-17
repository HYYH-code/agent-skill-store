# Agent Skill Store Security And Trust Model

Status: current.

This document states the trust assumptions and safety behavior for Agent Skill Store's native sync surface: the native manifest, skill archive artifacts, and sub-agent definitions. It is a threat model, not a vulnerability-disclosure policy.

## Scope

- Native manifest traversal (`/manifest.json` and linked documents).
- Skill artifacts (`skill-md` and `archive`) and their downloads.
- Sub-agent `agent-md` artifacts and their distribution.
- The `AgentSkillStore.Client` verified-download primitives and CLI sync commands.

Out of scope: how a consuming runtime executes synced content, and the local filesystem layout each client chooses.

## Trust Boundaries

| Boundary | Trusted party | Threat if violated |
|----------|---------------|--------------------|
| Client → server writes | Holders of a valid API key | Unauthorized publication or deletion of skills/sub-agents. |
| Server → client reads | The registry origin | Tampered or substituted artifact bytes. |
| Artifact author → consumer | Whoever can publish to the feed | Malicious skill scripts or sub-agent prompts distributed to every consumer. |
| Consumer runtime → host | The consuming client/agent | Unsafe archive extraction or execution of untrusted resources. |

The central assumption: **publishing to a feed is equivalent to influencing the behavior of every consumer that syncs it.** A registry's write access must be guarded accordingly.

## Authentication And Authorization

- Browser governance uses an HTTP-only local session cookie with role policies. Agent and CLI operations use a bearer API key (`Authorization: Bearer <key>`).
- API Keys are stored as SHA-256 hashes. Raw values are returned once and never included in list responses.
- The first management key is generated offline and imported through `AGENTSKILLSTORE__BOOTSTRAPAPIKEY` only when the key store is empty.
- API Key management requires `keys:manage`; the last unexpired management key cannot be revoked.
- Write endpoints require an authenticated author, reviewer, administrator, or appropriately scoped Agent Key. An empty key store never enables anonymous writes.
- Missing/empty/wrong-scheme credentials return **401**; a well-formed but invalid or expired key returns **403**.
- **Reads are open by default**: the manifest, RFC index, approved artifact endpoints (`SKILL.md`, `archive.zip`, per-file resources, `agent.md`, blobs), and public catalog do not require a key. A private registry that must gate reads should place Agent Skill Store behind a network boundary or an authenticating proxy.

## Integrity: Digest Verification

- Every artifact in the RFC feed and native manifest carries a `sha256:{hex}` digest of the exact bytes at its `url`.
- Clients **must** verify the digest before installing an artifact. `AgentSkillStore.Client` computes the hash while streaming the download and throws `InvalidDataException` on mismatch.
- The CLI `download-subagent` command stages the download to a temporary file, verifies the digest, and only then moves it into place; it never leaves a partial or unverified file, and refuses to overwrite an existing destination without `--force`.
- Because blobs are content-addressed by SHA-256, a substituted or corrupted artifact cannot match its advertised digest.

## Path Traversal And Resource Naming

- Resource relative paths are validated on upload and backfill: leading `/`, drive/scheme markers (`:`), NUL bytes, `.`/`..` segments, and bare root filenames are rejected.
- Resource downloads resolve by **exact stored relative-path equality**, not a filesystem join, so a traversal path simply fails to match and returns 404 rather than escaping the skill's resource set.
- Skill archives contain only regular-file entries. Symbolic links and reparse points are rejected at publish time and are never emitted into an archive, so the server cannot distribute a link-based escape.

## Archive Extraction Is The Consumer's Responsibility

Agent Skill Store builds archives from validated paths and **never extracts them**. `AgentSkillStore.Client` downloads and verifies archive **bytes only** — it does not unpack them.

Any consumer that extracts `archive.zip` owns the extraction safety and **must**:

- Reject entries whose resolved path escapes the destination directory (zip-slip).
- Treat archive members as untrusted content, including their preserved Unix permission bits.
- Apply its own policy for which resources may be marked executable or run.

## Executable Resource Modes

- Unix permission bits are preserved end-to-end (publish → archive → download), masked to standard permission bits (`0`–`511`). Type bits and setuid/setgid/sticky bits are stripped; a mode outside that range is rejected on upload.
- A synced executable resource therefore runs with the author's intended permissions. Consumers must treat such scripts as untrusted code and gate execution on feed trust.

## Sub-Agent Prompt Distribution

A sub-agent `agent-md` body becomes a **system prompt** inside the consuming runtime. Distributing a sub-agent is distributing executable instructions, and is a prompt-injection vector.

Trust assumptions for consumers:

- Sync sub-agents only from trusted feeds.
- Keep user-authored local definitions authoritative; server-synced files must never overwrite them.
- Install server-synced definitions into a managed, clearly-scoped location that can be pruned without touching user files.
- Review server-synced prompts as you would review third-party code.

## Sync Safety

- **Non-destructive**: a failed sync (network error, digest mismatch) leaves existing local artifacts in place.
- **Prune-after-confirm**: server-synced artifacts are removed only after a successful collection sync confirms the server no longer advertises them.
- **User files win**: user-authored local files take precedence over server-synced files with the same identity.
- **No prescribed paths**: the server protocol carries `name`, `version`, `kind`, `type`, `url`, and `digest`; local filesystem layout is entirely the client's choice.

## Forward Compatibility

Clients must ignore unsupported resource `kind`s, unknown artifact `type`s, and unrecognized fields rather than failing to parse them. This lets a newer server introduce collections or artifact types without breaking older clients, and keeps the RFC skill feed compatible for generic AgentSkills.io consumers.

## Summary Of Trust Assumptions

- The registry operator is trusted to control who can publish and delete.
- Artifact authors are trusted to the extent the feed's write access is controlled — publishing runs code and prompts on consumers.
- Transport integrity is enforced by digest verification, not assumed from the network.
- Consumers are responsible for safe extraction and execution of the verified bytes they receive.
