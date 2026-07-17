import { demoAudit, demoReviews, demoSkills, demoUsers } from './demo'
import type { ApiEnvelope, ApiKeySummary, AuditEvent, CreatedApiKey, GovernanceCategory, GovernanceTeam, ReviewItem, SessionUser, SkillSummary } from './types'

const demoEnabled = import.meta.env.VITE_AGENT_SKILL_STORE_DEMO === 'true'
const delay = (ms = 180) => new Promise((resolve) => setTimeout(resolve, ms))

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = init?.body instanceof FormData
    ? init?.headers
    : { 'Content-Type': 'application/json', ...init?.headers }
  const response = await fetch(path, {
    credentials: 'include',
    headers,
    ...init,
  })
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.error?.message || body?.message || `请求失败 (${response.status})`)
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  if (!text) return undefined as T
  const body = JSON.parse(text) as ApiEnvelope<T> | T
  return typeof body === 'object' && body !== null && 'data' in body ? (body as ApiEnvelope<T>).data : body as T
}

export const api = {
  async login(username: string, password: string): Promise<SessionUser> {
    if (demoEnabled) {
      await delay()
      const entry = demoUsers[username]
      if (!entry || entry.password !== password) throw new Error('用户名或密码错误')
      localStorage.setItem('skillstore-demo-user', JSON.stringify(entry.user))
      return entry.user
    }
    return request('/api/enterprise/v1/session/login', { method: 'POST', body: JSON.stringify({ username, password }) })
  },
  async logout(): Promise<void> {
    localStorage.removeItem('skillstore-demo-user')
    if (!demoEnabled) await request('/api/enterprise/v1/session/logout', { method: 'POST' })
  },
  async session(): Promise<SessionUser | null> {
    if (demoEnabled) {
      const raw = localStorage.getItem('skillstore-demo-user')
      return raw ? JSON.parse(raw) : null
    }
    try { return await request('/api/enterprise/v1/session') } catch { return null }
  },
  async skills(): Promise<SkillSummary[]> {
    if (demoEnabled) { await delay(); return demoSkills }
    return request('/api/enterprise/v1/skills')
  },
  async skill(name: string): Promise<SkillSummary> {
    if (demoEnabled) {
      await delay()
      const skill = demoSkills.find((item) => item.name === name)
      if (!skill) throw new Error('未找到该 AI 工装')
      return skill
    }
    return request(`/api/enterprise/v1/skills/${encodeURIComponent(name)}`)
  },
  async reviews(): Promise<ReviewItem[]> {
    if (demoEnabled) { await delay(); return demoReviews }
    return request('/api/enterprise/v1/reviews')
  },
  async decideReview(id: string, decision: 'approve' | 'reject', comment: string, channel?: 'beta' | 'stable'): Promise<void> {
    if (demoEnabled) { await delay(); return }
    await request(`/api/enterprise/v1/reviews/${id}/${decision}`, { method: 'POST', body: JSON.stringify({ comment, channel }) })
  },
  async audit(): Promise<AuditEvent[]> {
    if (demoEnabled) { await delay(); return demoAudit }
    return request('/api/enterprise/v1/audit')
  },
  async createPublication(payload: Record<string, unknown> & { package?: File }): Promise<void> {
    if (demoEnabled) { await delay(350); return }
    const form = new FormData()
    for (const [key, value] of Object.entries(payload)) {
      if (value !== undefined && value !== null) form.append(key, value instanceof File ? value : String(value))
    }
    await request('/api/enterprise/v1/publications', { method: 'POST', body: form })
  },
  async createInstallReference(skill: SkillSummary, version: string) {
    if (demoEnabled) {
      await delay()
      const selected = skill.versions.find((item) => item.version === version) ?? skill.versions[0]
      return {
        registry: 'skillstore', skill: `${skill.namespace}/${skill.name}`, version,
        digest: selected.digest, sourceUrl: `${location.origin}/api/v1/skills/${skill.name}/${version}/archive.zip`,
        requestedPermissions: skill.permissions,
      }
    }
    return request<Record<string, unknown>>(`/api/enterprise/v1/skills/${skill.name}/install-reference?version=${version}`)
  },
  async apiKeys(): Promise<ApiKeySummary[]> {
    if (demoEnabled) return [{ id: 1, label: 'Build Agent', agentId: 'agent-demo-build', machineHash: 'demo-machine', scopes: ['skills:read', 'skills:submit'], createdAt: '2026-07-10T08:00:00Z', expiresAt: '2026-12-31T00:00:00Z' }]
    return request('/api/enterprise/v1/api-keys')
  },
  async createApiKey(label: string): Promise<CreatedApiKey> {
    if (demoEnabled) return { id: Date.now(), label, key: `sk-skillstore-${crypto.randomUUID().replaceAll('-', '')}`, agentId: `agent-${crypto.randomUUID().slice(0, 8)}`, machineHash: 'demo-machine', scopes: ['skills:read', 'skills:submit'], createdAt: new Date().toISOString() }
    return request('/api/enterprise/v1/api-keys', { method: 'POST', body: JSON.stringify({ label }) })
  },
  async deleteApiKey(id: number): Promise<void> {
    if (demoEnabled) { await delay(); return }
    await request(`/api/enterprise/v1/api-keys/${id}`, { method: 'DELETE' })
  },
  async teams(): Promise<GovernanceTeam[]> {
    if (demoEnabled) return [{ id: 'team-platform', name: 'Platform Team', code: 'platform', status: 'active' }, { id: 'team-devex', name: 'Developer Experience', code: 'devex', status: 'active' }, { id: 'team-quality', name: 'Quality Engineering', code: 'quality', status: 'active' }]
    return request('/api/enterprise/v1/teams')
  },
  async saveTeam(team: GovernanceTeam): Promise<void> {
    if (demoEnabled) { await delay(); return }
    await request(`/api/enterprise/v1/teams/${team.id}`, { method: 'PUT', body: JSON.stringify(team) })
  },
  async categories(): Promise<GovernanceCategory[]> {
    if (demoEnabled) return [{ id: 'diagnosis', name: '问题诊断', sortOrder: 10 }, { id: 'quality', name: '研发质量', sortOrder: 20 }, { id: 'document', name: '文档处理', sortOrder: 30 }, { id: 'security', name: '安全合规', sortOrder: 40 }]
    return request('/api/enterprise/v1/categories')
  },
  async saveCategory(category: GovernanceCategory): Promise<void> {
    if (demoEnabled) { await delay(); return }
    await request(`/api/enterprise/v1/categories/${category.id}`, { method: 'PUT', body: JSON.stringify(category) })
  },
}
