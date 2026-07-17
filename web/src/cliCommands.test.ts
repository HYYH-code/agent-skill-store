import { describe, expect, it } from 'vitest'
import { cliAgentTargets, createCliCommand, createCliCommandSet, createSkillReference } from './cliCommands'

describe('Agent Skill Store CLI command helpers', () => {
  it('builds versioned skill references', () => {
    expect(createSkillReference('connected', 'vehicle-log-diagnosis', '1.2.3')).toBe('connected/vehicle-log-diagnosis@1.2.3')
    expect(createSkillReference('connected', 'vehicle-log-diagnosis')).toBe('connected/vehicle-log-diagnosis')
  })

  it('builds install, upgrade, uninstall and rollback commands for a target agent', () => {
    const reference = 'connected/vehicle-log-diagnosis@1.2.3'
    expect(createCliCommand('install', reference, 'codex')).toBe('skillstore install connected/vehicle-log-diagnosis@1.2.3 --target codex')
    expect(createCliCommand('update', reference, 'codex')).toBe('skillstore update connected/vehicle-log-diagnosis --target codex')
    expect(createCliCommand('uninstall', reference, 'codex')).toBe('skillstore uninstall connected/vehicle-log-diagnosis --target codex')
    expect(createCliCommand('rollback', reference, 'codex')).toBe('skillstore rollback connected/vehicle-log-diagnosis --version 1.2.3 --target codex')
  })

  it('exposes supported local agent targets', () => {
    const commands = createCliCommandSet('quality/code-review@2.1.0', 'claude')
    expect(commands.install).toContain('--target claude')
    expect(commands.rollback).toContain('--version 2.1.0')
    expect(cliAgentTargets.map((target) => target.id)).toEqual(['codex', 'claude', 'pi'])
    expect(cliAgentTargets.find((target) => target.id === 'pi')?.skillRoot).toBe('~/.pi/agent/skills')
  })
})
