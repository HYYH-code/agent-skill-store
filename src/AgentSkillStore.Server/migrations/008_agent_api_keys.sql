-- 008_agent_api_keys.sql: Bind API keys to Agent/device identity and submitter ownership.

ALTER TABLE api_keys ADD COLUMN agent_id TEXT NOT NULL DEFAULT 'legacy-agent';
ALTER TABLE api_keys ADD COLUMN machine_hash TEXT NOT NULL DEFAULT '';
ALTER TABLE api_keys ADD COLUMN scopes TEXT NOT NULL DEFAULT 'skills:read skills:submit';

ALTER TABLE publication ADD COLUMN submitter_id TEXT NOT NULL DEFAULT '';
UPDATE publication SET submitter_id = submitter WHERE submitter_id = '';

UPDATE team SET name = '待认领' WHERE id = 'team-unclaimed';
UPDATE team SET name = 'Platform Team' WHERE id = 'team-platform';
UPDATE team SET name = 'Developer Experience' WHERE id = 'team-devex';
UPDATE team SET name = 'Quality Engineering' WHERE id = 'team-quality';

UPDATE category SET name = '通用工装' WHERE id = 'general';
UPDATE category SET name = '智驾网联' WHERE id = 'connected';
UPDATE category SET name = '研发效能' WHERE id = 'engineering';
UPDATE category SET name = '车端研发' WHERE id = 'vehicle';
UPDATE category SET name = '产品研发' WHERE id = 'product';
UPDATE category SET name = '数据处理' WHERE id = 'data';
UPDATE category SET name = '问题诊断' WHERE id = 'diagnosis';
UPDATE category SET name = '研发质量' WHERE id = 'quality';
UPDATE category SET name = '文档处理' WHERE id = 'document';
UPDATE category SET name = '安全合规' WHERE id = 'security';
