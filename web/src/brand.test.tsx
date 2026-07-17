import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { LogoLockup, LogoMark, LogoWordmark } from './brand'

describe('brand logo components', () => {
  it('renders the deterministic SVG mark', () => {
    const markup = renderToStaticMarkup(<LogoMark />)
    expect(markup).toContain('src="/assets/agent-skill-store-mark.svg"')
    expect(markup).toContain('alt="Agent Skill Store mark"')
  })

  it('uses the standard and inverse lockups', () => {
    const standard = renderToStaticMarkup(<LogoWordmark />)
    const inverse = renderToStaticMarkup(<LogoWordmark variant="white" />)
    expect(standard).toContain('src="/assets/agent-skill-store-lockup.svg"')
    expect(inverse).toContain('src="/assets/agent-skill-store-lockup-inverse.svg"')
  })

  it('uses the mark only for compact navigation', () => {
    const compact = renderToStaticMarkup(<LogoLockup compact />)
    const full = renderToStaticMarkup(<LogoLockup />)
    expect(compact).toContain('agent-skill-store-mark.svg')
    expect(compact).not.toContain('agent-skill-store-lockup.svg')
    expect(full).toContain('agent-skill-store-lockup.svg')
  })
})
