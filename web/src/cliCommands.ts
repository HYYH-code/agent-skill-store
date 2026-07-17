export type CliAgentId = 'codex' | 'claude' | 'pi'
export type CliAction = 'install' | 'update' | 'uninstall' | 'rollback'

export type CliAgentTarget = {
  id: CliAgentId
  label: string
  skillRoot: string
}

export const cliAgentTargets: CliAgentTarget[] = [
  { id: 'codex', label: 'Codex', skillRoot: '~/.codex/skills' },
  { id: 'claude', label: 'Claude Code', skillRoot: '~/.claude/skills' },
  { id: 'pi', label: 'Pi', skillRoot: '~/.pi/agent/skills' },
]

export function createSkillReference(namespace: string, name: string, version?: string) {
  const base = namespace ? `${namespace}/${name}` : name
  return version ? `${base}@${version}` : base
}

export function createCliCommand(action: CliAction, reference: string, agent: CliAgentId) {
  const [baseReference, version] = reference.split('@')
  if (action === 'uninstall') return `skillstore uninstall ${baseReference} --target ${agent}`
  if (action === 'update') return `skillstore update ${baseReference} --target ${agent}`
  if (action === 'rollback') return `skillstore rollback ${baseReference}${version ? ` --version ${version}` : ''} --target ${agent}`
  return `skillstore install ${reference} --target ${agent}`
}

export function createCliCommandSet(reference: string, agent: CliAgentId) {
  return {
    install: createCliCommand('install', reference, agent),
    update: createCliCommand('update', reference, agent),
    uninstall: createCliCommand('uninstall', reference, agent),
    rollback: createCliCommand('rollback', reference, agent),
  }
}
