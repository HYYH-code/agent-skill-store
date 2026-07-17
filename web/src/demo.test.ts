import { describe, expect, it } from 'vitest'
import { demoSkills } from './demo'

describe('demo skill fixtures', () => {
  it('provides unique skill ids and names', () => {
    expect(new Set(demoSkills.map((skill) => skill.id)).size).toBe(demoSkills.length)
    expect(new Set(demoSkills.map((skill) => skill.name)).size).toBe(demoSkills.length)
  })

  it('keeps recommended versions installable', () => {
    for (const skill of demoSkills) {
      const recommended = skill.versions.find((version) => version.version === skill.recommendedVersion)
      expect(recommended).toBeDefined()
      expect(recommended?.status).not.toBe('revoked')
      expect(recommended?.status).not.toBe('draft')
      expect(recommended?.status).not.toBe('pending')
    }
  })

  it('declares permissions explicitly', () => {
    for (const skill of demoSkills) {
      expect(skill.permissions.network.policy).toBeTruthy()
      expect(Array.isArray(skill.permissions.filesystem.read)).toBe(true)
      expect(Array.isArray(skill.permissions.filesystem.write)).toBe(true)
    }
  })
})
