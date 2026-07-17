-- 007_enterprise_governance.sql: Agent Skill Store企业元数据、发布审核和审计扩展

CREATE TABLE IF NOT EXISTS enterprise_skill (
    skill_name TEXT PRIMARY KEY COLLATE NOCASE,
    display_name TEXT NOT NULL,
    namespace TEXT NOT NULL DEFAULT 'general',
    owner_team_id TEXT NOT NULL,
    category_id TEXT NOT NULL,
    visibility TEXT NOT NULL DEFAULT 'internal' CHECK(visibility IN ('internal', 'team')),
    featured INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS enterprise_skill_version (
    skill_name TEXT NOT NULL COLLATE NOCASE,
    version TEXT NOT NULL,
    digest TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'beta' CHECK(status IN ('draft', 'pending', 'beta', 'stable', 'deprecated', 'revoked', 'imported')),
    risk_level TEXT NOT NULL DEFAULT 'medium' CHECK(risk_level IN ('low', 'medium', 'high', 'critical')),
    manifest_json TEXT NOT NULL DEFAULT '{}',
    changelog TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY(skill_name, version)
);

CREATE TABLE IF NOT EXISTS publication (
    id TEXT PRIMARY KEY,
    skill_name TEXT NOT NULL COLLATE NOCASE,
    version TEXT NOT NULL,
    display_name TEXT NOT NULL,
    namespace TEXT NOT NULL,
    owner_team_id TEXT NOT NULL,
    submitter TEXT NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('draft', 'pending', 'approved', 'rejected')),
    risk_level TEXT NOT NULL,
    package_digest TEXT NOT NULL,
    network_policy TEXT NOT NULL DEFAULT 'deny-all',
    changelog TEXT NOT NULL,
    created_at TEXT NOT NULL,
    submitted_at TEXT
);

CREATE TABLE IF NOT EXISTS review (
    id TEXT PRIMARY KEY,
    publication_id TEXT NOT NULL REFERENCES publication(id),
    reviewer TEXT,
    decision TEXT CHECK(decision IN ('approved', 'rejected')),
    channel TEXT CHECK(channel IN ('beta', 'stable')),
    comment TEXT,
    decided_at TEXT
);

CREATE TABLE IF NOT EXISTS team (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    code TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE IF NOT EXISTS category (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    parent_id TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS role_binding (
    subject TEXT NOT NULL,
    role TEXT NOT NULL,
    scope_type TEXT NOT NULL DEFAULT 'platform',
    scope_id TEXT NOT NULL DEFAULT '*',
    PRIMARY KEY(subject, role, scope_type, scope_id)
);

CREATE TABLE IF NOT EXISTS audit_event (
    id TEXT PRIMARY KEY,
    actor TEXT NOT NULL,
    role TEXT NOT NULL,
    action TEXT NOT NULL,
    resource TEXT NOT NULL,
    result TEXT NOT NULL,
    request_id TEXT NOT NULL,
    detail_json TEXT NOT NULL DEFAULT '{}',
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_audit_event_created_at ON audit_event(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_publication_state ON publication(state, created_at DESC);

CREATE TABLE IF NOT EXISTS workspace_installation (
    work_id TEXT NOT NULL,
    skill_name TEXT NOT NULL COLLATE NOCASE,
    version TEXT NOT NULL,
    digest TEXT NOT NULL,
    status TEXT NOT NULL,
    installed_by TEXT NOT NULL,
    installed_at TEXT NOT NULL,
    operation_id TEXT NOT NULL UNIQUE,
    PRIMARY KEY(work_id, skill_name)
);

INSERT OR IGNORE INTO team(id, name, code, status) VALUES
    ('team-unclaimed', '待认领', 'unclaimed', 'active'),
    ('team-platform', 'Platform Team', 'platform', 'active'),
    ('team-devex', 'Developer Experience', 'developer-experience', 'active'),
    ('team-quality', 'Quality Engineering', 'quality-engineering', 'active');

INSERT OR IGNORE INTO category(id, name, parent_id, sort_order) VALUES
    ('general', '通用工装', NULL, 0),
    ('connected', '智驾网联', NULL, 5),
    ('engineering', '研发效能', NULL, 6),
    ('vehicle', '车端研发', NULL, 7),
    ('product', '产品研发', NULL, 8),
    ('data', '数据处理', NULL, 9),
    ('diagnosis', '问题诊断', NULL, 10),
    ('quality', '研发质量', NULL, 20),
    ('document', '文档处理', NULL, 30),
    ('security', '安全合规', NULL, 40);

INSERT OR IGNORE INTO enterprise_skill(skill_name, display_name, namespace, owner_team_id, category_id, visibility, featured, created_at, updated_at)
SELECT s.name, s.name, COALESCE(NULLIF(sv.category, ''), 'general'), 'team-unclaimed', COALESCE(NULLIF(sv.category, ''), 'general'), 'internal', 0, s.created_at, s.updated_at
FROM skills s
JOIN skill_versions sv ON sv.skill_id = s.id AND sv.is_latest = 1;

INSERT OR IGNORE INTO enterprise_skill_version(skill_name, version, digest, status, risk_level, manifest_json, changelog, created_at, updated_at)
SELECT s.name, sv.version, sv.sha256, 'beta', 'medium', '{}', '', sv.published_at, sv.published_at
FROM skills s
JOIN skill_versions sv ON sv.skill_id = s.id;
