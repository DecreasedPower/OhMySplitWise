import { useSyncExternalStore } from 'react'

let required = false
const listeners = new Set<() => void>()

export function requireClientUpgrade() {
  if (required) return
  required = true
  listeners.forEach((listener) => listener())
}

export function isClientUpgradeRequired() {
  return required
}

export function resetClientUpgradeRequired() {
  required = false
}

export function useClientUpgradeRequired() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => { listeners.delete(listener) }
    },
    () => required,
    () => false,
  )
}
