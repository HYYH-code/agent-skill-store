# Agent Skill Store

**Discover. Govern. Install.**

Agent Skill Store 是一个可自托管的 Agent Skill 目录、治理后台、API 和 CLI。它支持公开浏览、受控上传、独立审核、版本管理、审计，以及由用户或 Agent 在本机完成安装、升级、卸载和回滚。

服务端不会执行 Skill，也不会远程写入用户的 Agent 目录。本机文件变更只会在用户或 Agent 调用 `skillstore` 时发生。

## 环境要求

- .NET SDK `10.0.203`
- Node.js `24`
- npm

## 本地启动

```bash
cd web
npm ci
npm run build
cd ..

dotnet run --project src/AgentSkillStore.Cli -- api-key generate

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://0.0.0.0:8081
export AGENTSKILLSTORE__DATAPATH="$PWD/data"
export AGENTSKILLSTORE__BASEURL=http://127.0.0.1:8081
export AGENTSKILLSTORE__BOOTSTRAPAPIKEY='<上一步生成的 Key>'

dotnet restore src/AgentSkillStore.Server/AgentSkillStore.Server.csproj
dotnet run --project src/AgentSkillStore.Server -c Release
```

访问 [http://localhost:8081](http://localhost:8081)。

开发环境内置账号：

| 角色 | 账号 | 密码 |
| --- | --- | --- |
| 平台管理员 | `admin` | `admin` |
| Skill 作者 | `author` | `author` |
| Skill 审核员 | `reviewer` | `reviewer` |
| 工程师 | `engineer` | `engineer` |

Production 不加载默认账号，并拒绝长度不足 12 位的本地账号密码。

## CLI 使用

```bash
export AGENT_SKILL_STORE_URL=http://localhost:8081
export AGENT_SKILL_STORE_API_KEY='<Bootstrap Key>'

skillstore api-key create --label 'Build Agent'
skillstore list
skillstore install engineering/code-review-checklist@2.1.0 --target codex
skillstore list installed --target codex
```

CLI 配置存放在 `~/.agent-skill-store/config.json`。原始 API Key 只返回一次，服务端仅保存 SHA-256。

## 核心边界

- 公开用户：浏览已发布 Skill、版本与下载制品。
- 作者：上传 Skill 包并提交审核。
- 审核员：独立批准或拒绝，不能审核本人提交。
- 平台管理员：管理团队、分类、账号、API Key 和审计。
- CLI：在本机安装、升级、卸载与回滚 Skill。

## 文档

- [CLI](docs/CLI.md)
- [部署](docs/DEPLOYMENT.md)
- [Agent 接入](docs/AGENT_INTEGRATION.md)
- [协议规格](docs/specs/README.md)
- [安全策略](SECURITY.md)
- [参与贡献](CONTRIBUTING.md)

## 许可证

项目采用 Apache-2.0。上游归属和保留声明见 [NOTICE](NOTICE) 与 [UPSTREAM.md](UPSTREAM.md)。
