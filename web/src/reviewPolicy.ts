import type { PermissionSet, RiskLevel } from './types'

export type PermissionConfirmation = {
  value: string
  label: string
}

export function requiresPermissionConfirmation(riskLevel: RiskLevel) {
  return riskLevel === 'high' || riskLevel === 'critical'
}

export function createPermissionConfirmations(permissions: PermissionSet): PermissionConfirmation[] {
  return [
    ...permissions.filesystem.read.map((path) => ({ value: `read:${path}`, label: `确认文件读取范围：${path}` })),
    ...permissions.filesystem.write.map((path) => ({ value: `write:${path}`, label: `确认文件写入范围：${path}` })),
    ...(permissions.network.policy === 'deny-all'
      ? []
      : [{ value: `network:${permissions.network.policy}`, label: `确认网络策略：${permissions.network.policy}` }]),
    ...permissions.network.allow.map((host) => ({ value: `network-host:${host}`, label: `确认网络目标：${host}` })),
    ...permissions.commands.map((command) => ({ value: `command:${command}`, label: `确认系统命令：${command}` })),
    ...permissions.secrets.map((secret) => ({ value: `secret:${secret}`, label: `确认密钥引用：${secret}` })),
  ]
}

export function hasConfirmedAll(required: PermissionConfirmation[], confirmed: string[]) {
  return required.every((item) => confirmed.includes(item.value))
}
