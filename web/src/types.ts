export type SkillStatus = 'draft' | 'pending' | 'beta' | 'stable' | 'deprecated' | 'revoked'
export type RiskLevel = 'low' | 'medium' | 'high' | 'critical'
export type Role = 'engineer' | 'author' | 'review_admin' | 'platform_admin'

export interface SessionUser {
  id: string
  displayName: string
  username: string
  roles: Role[]
  team: string
}

export interface PermissionSet {
  filesystem: { read: string[]; write: string[] }
  network: { policy: 'deny-all' | 'allow-list' | 'unrestricted'; allow: string[] }
  commands: string[]
  secrets: string[]
}

export interface SkillVersion {
  version: string
  status: SkillStatus
  digest: string
  publishedAt: string
  changelog: string
  sizeBytes: number
  fileCount: number
}

export interface SkillSummary {
  id: string
  name: string
  namespace: string
  displayName: string
  description: string
  owner: { id: string; name: string }
  category: { id: string; name: string }
  latestVersion: string
  recommendedVersion: string
  status: SkillStatus
  riskLevel: RiskLevel
  runtime: string[]
  permissions: PermissionSet
  digest: string
  updatedAt: string
  featured?: boolean
  versions: SkillVersion[]
  skillMarkdown: string
  files: { path: string; sizeBytes: number; sha256: string }[]
  reviewHistory?: ReviewHistoryEntry[]
}

export interface ReviewHistoryEntry {
  id: string
  version: string
  submitter: string
  submittedAt: string
  state: 'pending' | 'approved' | 'rejected'
  reviewer?: string
  decision?: 'approved' | 'rejected'
  channel?: 'beta' | 'stable'
  comment?: string
  decidedAt?: string
}

export interface ReviewItem {
  id: string
  publicationId: string
  skillName: string
  displayName: string
  version: string
  submitter: string
  riskLevel: RiskLevel
  submittedAt: string
  state: 'pending' | 'approved' | 'rejected'
  permissions: PermissionSet
  comment?: string
}

export interface AuditEvent {
  id: string
  actor: string
  role: string
  action: string
  resource: string
  result: 'success' | 'denied' | 'failed'
  requestId: string
  createdAt: string
}

export interface ApiEnvelope<T> {
  data: T
  meta: { requestId: string; timestamp: string }
  error: null | { code: string; message: string }
}

export interface ApiKeySummary {
  id: number
  label: string
  agentId: string
  machineHash: string
  scopes: string[]
  createdAt: string
  expiresAt?: string
}

export interface CreatedApiKey extends ApiKeySummary {
  key: string
}

export interface GovernanceTeam {
  id: string
  name: string
  code: string
  status: string
}

export interface GovernanceCategory {
  id: string
  name: string
  parentId?: string
  sortOrder: number
}
