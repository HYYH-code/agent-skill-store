export type CliAgentId = 'all' | 'agents' | 'codex' | 'claude' | 'pi' | 'opencode'
export type CliAction = 'install' | 'update' | 'uninstall' | 'rollback'

export type CliAgentTarget = {
  id: CliAgentId
  label: string
  skillRoot: string
}

export const cliAgentTargets: CliAgentTarget[] = [
  { id: 'all', label: '全部 Agent', skillRoot: '~/.agents/skills' },
  { id: 'agents', label: '通用 .agents', skillRoot: '~/.agents/skills' },
  { id: 'codex', label: 'Codex（共享目录）', skillRoot: '~/.agents/skills' },
  { id: 'claude', label: 'Claude Code（桥接）', skillRoot: '~/.claude/skills' },
  { id: 'pi', label: 'Pi（共享目录）', skillRoot: '~/.agents/skills' },
  { id: 'opencode', label: 'OpenCode（共享目录）', skillRoot: '~/.agents/skills' },
]

export function createSkillReference(namespace: string, name: string, version?: string) {
  const base = namespace ? `${namespace}/${name}` : name
  return version ? `${base}@${version}` : base
}

export function createCliCommand(action: CliAction, reference: string, agent: CliAgentId) {
  const [baseReference, version] = reference.split('@')
  const target = agent === 'all' ? '' : ` --target ${agent}`
  if (action === 'uninstall') return `skillstore uninstall ${baseReference}${target}`
  if (action === 'update') return `skillstore update ${baseReference}${target}`
  if (action === 'rollback') return `skillstore rollback ${baseReference}${version ? ` --version ${version}` : ''}${target}`
  return `skillstore install ${reference}${target}`
}

export function createCliCommandSet(reference: string, agent: CliAgentId) {
  return {
    install: createCliCommand('install', reference, agent),
    update: createCliCommand('update', reference, agent),
    uninstall: createCliCommand('uninstall', reference, agent),
    rollback: createCliCommand('rollback', reference, agent),
  }
}
