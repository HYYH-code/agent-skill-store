# Security Policy

## Supported Version

Security fixes are provided for the latest tagged release and the current `main` branch.

## Reporting A Vulnerability

Do not open a public issue for a suspected vulnerability. Use GitHub's private vulnerability reporting feature for the repository. Include affected versions, reproduction steps, impact, and any proposed mitigation.

## Credential Rules

- Generate the first Bootstrap Key offline with `skillstore api-key generate`.
- Store API Keys in a secret manager.
- Never commit keys, include them in issue logs, or pass them in URLs.
- Raw keys are returned once; the server stores only SHA-256 hashes.
- Rotate a key by creating a replacement before revoking the old one.

## Deployment Rules

- Terminate TLS in production.
- Use strong local account passwords and restrict the governance console.
- Back up the complete data directory.
- Keep direct publishing disabled unless an explicit migration requires it.
- Review Skills as untrusted code and prompts before approval.

The detailed trust model is documented in [docs/specs/security.md](docs/specs/security.md).
