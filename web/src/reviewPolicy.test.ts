import { describe, expect, it } from 'vitest'
import { createPermissionConfirmations, hasConfirmedAll, requiresPermissionConfirmation } from './reviewPolicy'

describe('review permission policy', () => {
  const permissions = {
    filesystem: { read: ['work/**'], write: ['work/output/**'] },
    network: { policy: 'allow-list' as const, allow: ['api.internal.example'] },
    commands: ['dotnet'],
    secrets: ['DEPLOY_TOKEN'],
  }

  it('requires confirmations for high and critical risk reviews', () => {
    expect(requiresPermissionConfirmation('medium')).toBe(false)
    expect(requiresPermissionConfirmation('high')).toBe(true)
    expect(requiresPermissionConfirmation('critical')).toBe(true)
  })

  it('requires every declared high-risk permission to be confirmed', () => {
    const required = createPermissionConfirmations(permissions)
    expect(required.map((item) => item.value)).toEqual([
      'read:work/**',
      'write:work/output/**',
      'network:allow-list',
      'network-host:api.internal.example',
      'command:dotnet',
      'secret:DEPLOY_TOKEN',
    ])
    expect(hasConfirmedAll(required, required.slice(0, -1).map((item) => item.value))).toBe(false)
    expect(hasConfirmedAll(required, required.map((item) => item.value))).toBe(true)
  })
})
