export type LogoVariant = 'full-color' | 'dark' | 'white'

type LogoMarkProps = {
  variant?: LogoVariant
  size?: number
  title?: string
  className?: string
}

type LogoWordmarkProps = {
  variant?: LogoVariant
  className?: string
}

type LogoLockupProps = {
  variant?: LogoVariant
  compact?: boolean
  className?: string
}

const markAsset = '/assets/agent-skill-store-mark.svg'
const lockupAsset = '/assets/agent-skill-store-lockup.svg'
const inverseLockupAsset = '/assets/agent-skill-store-lockup-inverse.svg'

function lockupFor(variant: LogoVariant) {
  return variant === 'white' ? inverseLockupAsset : lockupAsset
}

export function LogoMark({ size = 52, title = 'Agent Skill Store mark', className }: LogoMarkProps) {
  return <img
    className={className}
    src={markAsset}
    width={size}
    height={size}
    alt={title}
    aria-hidden={title ? undefined : true}
    draggable={false}
  />
}

export function LogoWordmark({ variant = 'full-color', className }: LogoWordmarkProps) {
  return <img
    className={className}
    src={lockupFor(variant)}
    alt="Agent Skill Store"
    draggable={false}
  />
}

export function LogoLockup({ variant = 'full-color', compact = false, className }: LogoLockupProps) {
  return <div className={`brand-lockup ${compact ? 'compact' : ''} ${className ?? ''}`.trim()}>
    {compact
      ? <LogoMark className="brand-logo-mark" />
      : <>
        <LogoWordmark variant={variant} className="brand-logo-lockup" />
        <LogoMark className="brand-logo-mark brand-logo-mark-alternate" />
      </>}
  </div>
}
