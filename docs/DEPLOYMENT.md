# Deployment

## Local Development

```bash
cd web
npm ci
npm run build
cd ..

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://0.0.0.0:8081
export AGENTSKILLSTORE__DATAPATH="$PWD/data"
export AGENTSKILLSTORE__BASEURL=http://127.0.0.1:8081
export AGENTSKILLSTORE__BOOTSTRAPAPIKEY='<generated-key>'

dotnet restore src/AgentSkillStore.Server/AgentSkillStore.Server.csproj
dotnet run --project src/AgentSkillStore.Server -c Release
```

Open `http://localhost:8081`. The container listens on `8080` internally; local host examples use `8081`.

## Production Requirements

Production startup fails unless:

- `AGENTSKILLSTORE__BASEURL` is an absolute non-loopback URL;
- at least one local authenticated user is configured;
- every configured local password contains at least 12 characters.

Production never loads the Development demo accounts.

## Docker Compose

```bash
export AGENT_SKILL_STORE_BASEURL=https://skills.example.com
export AGENT_SKILL_STORE_ADMIN_USERNAME=admin
export AGENT_SKILL_STORE_ADMIN_PASSWORD='replace-with-a-strong-password'
export AGENT_SKILL_STORE_BOOTSTRAP_API_KEY='<generated-key>'
docker compose -f docker/docker-compose.yml up --build -d
```

The Compose service uses a read-only root filesystem and persists application data in the `agent-skill-store-data` volume.

## Configuration

| Environment variable | Purpose |
| --- | --- |
| `AGENTSKILLSTORE__DATAPATH` | SQLite and blob data directory |
| `AGENTSKILLSTORE__BASEURL` | Public base URL used in generated links |
| `AGENTSKILLSTORE__BOOTSTRAPAPIKEY` | First offline-generated management key |
| `AGENTSKILLSTORE__SEEDDATA` | Load bundled sample Skills |
| `AGENTSKILLSTORE__AUTH__USERS__N__*` | Local user definitions |
| `AGENTSKILLSTORE__COMPATIBILITY__ALLOWDIRECTPUBLISH` | Enable migration-only direct publishing |

## Backups

Back up the entire configured data directory as one consistency unit. It contains the SQLite database and content-addressed blobs. Stop writes or use a filesystem/database snapshot that preserves consistency between both.

## Health

- `GET /health` provides a process health check.
- The web governance status page reports registry, database, and blob state.
- A reverse proxy should terminate TLS and set an appropriate request-size limit for Skill packages.
