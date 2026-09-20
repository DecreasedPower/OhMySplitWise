import { useSyncExternalStore } from 'react'

let expired = false
const listeners = new Set<() => void>()

export function expireSession() {
  if (expired) return
  expired = true
  listeners.forEach((listener) => listener())
}

export function useSessionExpired() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => { listeners.delete(listener) }
    },
    () => expired,
    () => false,
  )
}
