import type { AuditEvent, PermissionSet, ReviewItem, SessionUser, SkillSummary } from './types'

const safePermissions: PermissionSet = {
  filesystem: { read: ['work/**'], write: ['work/output/**'] },
  network: { policy: 'deny-all', allow: [] },
  commands: [],
  secrets: [],
}

const networkPermissions: PermissionSet = {
  filesystem: { read: ['work/logs/**'], write: ['work/output/**'] },
  network: { policy: 'allow-list', allow: ['diagnostics.example', 'telemetry.example'] },
  commands: ['python scripts/analyze.py'],
  secrets: ['DIAGNOSIS_TOKEN'],
}

const version = (value: string, status: SkillSummary['status'], date: string, digest: string) => ({
  version: value,
  status,
  digest,
  publishedAt: date,
  changelog: `${value} 完成规则更新、兼容性校验与使用说明补充。`,
  sizeBytes: 38240,
  fileCount: 8,
})

export const demoSkills: SkillSummary[] = [
  {
    id: 'skill-vehicle-log', name: 'vehicle-log-diagnosis', namespace: 'connected', displayName: '车端日志诊断工装',
    description: '解析设备日志、聚合异常信号并输出可追溯的问题线索。', owner: { id: 'team-platform', name: 'Platform Team' },
    category: { id: 'diagnosis', name: '问题诊断' }, latestVersion: '1.3.0', recommendedVersion: '1.2.3', status: 'stable', riskLevel: 'medium', runtime: ['Pi ≥ 0.1.0'], permissions: networkPermissions,
    digest: 'sha256:8d4a6f2b3c1e9a07', updatedAt: '2026-07-15T09:30:00Z', featured: true,
    versions: [version('1.3.0', 'beta', '2026-07-15T09:30:00Z', 'sha256:a712f9d1'), version('1.2.3', 'stable', '2026-07-10T05:20:00Z', 'sha256:8d4a6f2b'), version('1.1.0', 'deprecated', '2026-06-16T03:00:00Z', 'sha256:33ab120c')],
    skillMarkdown: '# 车端日志诊断工装\n\n面向智驾与网联研发场景，对上传的日志目录执行结构化分析。\n\n## 输入\n\n- `work/logs/**` 下的日志文件\n- 故障时间窗和车辆配置\n\n## 输出\n\n在 `work/output/diagnosis/` 生成摘要、证据索引和建议排查项。',
    files: [{ path: 'SKILL.md', sizeBytes: 4812, sha256: '8d4a6f2b' }, { path: 'manifest.yaml', sizeBytes: 1204, sha256: 'b1a934cc' }, { path: 'scripts/analyze.py', sizeBytes: 16422, sha256: '2d497aa1' }, { path: 'references/signals.md', sizeBytes: 7812, sha256: 'f19301ae' }],
  },
  {
    id: 'skill-code-review', name: 'code-review-checklist', namespace: 'engineering', displayName: '代码评审检查工装',
    description: '根据团队规范检查变更范围、异常处理、测试覆盖和安全风险。', owner: { id: 'team-devex', name: 'Developer Experience' }, category: { id: 'quality', name: '研发质量' }, latestVersion: '2.1.0', recommendedVersion: '2.1.0', status: 'stable', riskLevel: 'low', runtime: ['Pi'], permissions: safePermissions, digest: 'sha256:7cc9164d9820', updatedAt: '2026-07-14T04:20:00Z', featured: true, versions: [version('2.1.0', 'stable', '2026-07-14T04:20:00Z', 'sha256:7cc9164d'), version('2.0.0', 'deprecated', '2026-06-22T04:20:00Z', 'sha256:21bc11ae')], skillMarkdown: '# 代码评审检查工装\n\n对提交变更执行一致、可解释的工程检查。', files: [{ path: 'SKILL.md', sizeBytes: 5440, sha256: '7cc9164d' }, { path: 'references/checklist.md', sizeBytes: 8214, sha256: '9b3162df' }],
  },
  {
    id: 'skill-requirement', name: 'requirement-clarifier', namespace: 'product', displayName: '需求澄清工装',
    description: '将非结构化需求整理为范围、约束、验收标准和待决策事项。', owner: { id: 'team-product', name: 'Product Engineering' }, category: { id: 'document', name: '文档处理' }, latestVersion: '1.4.2', recommendedVersion: '1.4.2', status: 'stable', riskLevel: 'low', runtime: ['Pi'], permissions: safePermissions, digest: 'sha256:38f19d8a', updatedAt: '2026-07-12T08:12:00Z', featured: true, versions: [version('1.4.2', 'stable', '2026-07-12T08:12:00Z', 'sha256:38f19d8a')], skillMarkdown: '# 需求澄清工装\n\n把需求转化为可评审、可实现和可验收的交付规格。', files: [{ path: 'SKILL.md', sizeBytes: 3850, sha256: '38f19d8a' }],
  },
  {
    id: 'skill-can', name: 'can-signal-analyzer', namespace: 'vehicle', displayName: 'CAN 信号分析工装',
    description: '分析时序信号变化、边界条件和异常关联。', owner: { id: 'team-data', name: 'Data Engineering' }, category: { id: 'data', name: '数据处理' }, latestVersion: '0.9.0', recommendedVersion: '0.9.0', status: 'beta', riskLevel: 'medium', runtime: ['Pi'], permissions: safePermissions, digest: 'sha256:201f9d0c', updatedAt: '2026-07-11T11:20:00Z', versions: [version('0.9.0', 'beta', '2026-07-11T11:20:00Z', 'sha256:201f9d0c')], skillMarkdown: '# 信号分析工装\n\n当前为测试版本，请在非生产环境中验证。', files: [{ path: 'SKILL.md', sizeBytes: 3902, sha256: '201f9d0c' }],
  },
  {
    id: 'skill-testcase', name: 'testcase-generator', namespace: 'quality', displayName: '测试用例生成工装',
    description: '基于需求和接口定义生成分层测试用例及追踪矩阵。', owner: { id: 'team-quality', name: 'Quality Engineering' }, category: { id: 'quality', name: '研发质量' }, latestVersion: '1.0.1', recommendedVersion: '1.0.1', status: 'stable', riskLevel: 'low', runtime: ['Pi'], permissions: safePermissions, digest: 'sha256:aa109221', updatedAt: '2026-07-09T06:45:00Z', versions: [version('1.0.1', 'stable', '2026-07-09T06:45:00Z', 'sha256:aa109221')], skillMarkdown: '# 测试用例生成工装', files: [{ path: 'SKILL.md', sizeBytes: 2987, sha256: 'aa109221' }],
  },
  {
    id: 'skill-security', name: 'secret-scanner', namespace: 'security', displayName: '敏感信息扫描工装',
    description: '识别提交内容中的密钥、Token、私钥和固定凭证。', owner: { id: 'team-security', name: 'Security Engineering' }, category: { id: 'security', name: '安全合规' }, latestVersion: '1.1.0', recommendedVersion: '1.1.0', status: 'stable', riskLevel: 'medium', runtime: ['Pi'], permissions: safePermissions, digest: 'sha256:18fe228b', updatedAt: '2026-07-08T03:30:00Z', versions: [version('1.1.0', 'stable', '2026-07-08T03:30:00Z', 'sha256:18fe228b')], skillMarkdown: '# 敏感信息扫描工装', files: [{ path: 'SKILL.md', sizeBytes: 4410, sha256: '18fe228b' }],
  },
]

export const demoUsers: Record<string, { password: string; user: SessionUser }> = {
  admin: { password: 'admin', user: { id: 'u-admin', username: 'admin', displayName: '平台管理员', roles: ['platform_admin'], team: 'Platform Team' } },
  author: { password: 'author', user: { id: 'u-author', username: 'author', displayName: 'Skill 作者', roles: ['author'], team: 'Developer Experience' } },
  reviewer: { password: 'reviewer', user: { id: 'u-reviewer', username: 'reviewer', displayName: 'Skill 审核员', roles: ['review_admin'], team: 'Quality Engineering' } },
  engineer: { password: 'engineer', user: { id: 'u-engineer', username: 'engineer', displayName: '研发工程师', roles: ['engineer'], team: 'Product Engineering' } },
}

export const demoReviews: ReviewItem[] = [
  { id: 'review-001', publicationId: 'pub-001', skillName: 'can-signal-analyzer', displayName: '信号分析工装', version: '1.0.0', submitter: 'Skill Author', riskLevel: 'medium', submittedAt: '2026-07-15T08:35:00Z', state: 'pending', permissions: safePermissions },
  { id: 'review-002', publicationId: 'pub-002', skillName: 'vehicle-log-diagnosis', displayName: '设备日志诊断工装', version: '1.3.0', submitter: 'Build Engineer', riskLevel: 'high', submittedAt: '2026-07-15T07:20:00Z', state: 'pending', permissions: networkPermissions },
]

export const demoAudit: AuditEvent[] = [
  { id: 'audit-1', actor: 'Skill Reviewer', role: 'review_admin', action: 'skill.version.approve', resource: 'engineering/code-review-checklist@2.1.0', result: 'success', requestId: 'req-8921', createdAt: '2026-07-14T04:20:00Z' },
  { id: 'audit-2', actor: 'Skill Author', role: 'author', action: 'publication.submit', resource: 'data/can-signal-analyzer@1.0.0', result: 'success', requestId: 'req-8912', createdAt: '2026-07-15T08:35:00Z' },
  { id: 'audit-3', actor: '研发工程师', role: 'engineer', action: 'workspace.install', resource: 'connected/vehicle-log-diagnosis@1.2.3', result: 'success', requestId: 'req-8876', createdAt: '2026-07-13T02:16:00Z' },
]

export const statusLabels = { draft: '草稿', pending: '待审核', beta: '测试版', stable: '已认证', deprecated: '已弃用', revoked: '已撤回' } as const
export const roleLabels = { engineer: '研发工程师', author: '工装作者', review_admin: '审核管理员', platform_admin: '平台管理员' } as const
