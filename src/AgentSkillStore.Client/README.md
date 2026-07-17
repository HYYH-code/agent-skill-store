# AgentSkillStore.Client

`AgentSkillStore.Client` is the typed .NET client for Agent Skill Store.

```bash
dotnet add package AgentSkillStore.Client
```

```csharp
using AgentSkillStore.Client;

using var client = new AgentSkillStoreClient("https://skills.example.com");
var skills = await client.ListSkillsAsync();
var latest = await client.GetLatestVersionAsync("code-review-checklist");
```

Provide an Agent Key for authenticated publishing and key-management operations:

```csharp
using var client = new AgentSkillStoreClient(
    "https://skills.example.com",
    Environment.GetEnvironmentVariable("AGENT_SKILL_STORE_API_KEY"));
```

The client is AOT-compatible, uses source-generated JSON serialization, follows the public discovery APIs, and includes verified artifact download helpers.
