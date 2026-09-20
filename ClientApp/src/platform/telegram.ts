import { useEffect, useSyncExternalStore } from 'react'

const telegram = () => window.Telegram?.WebApp

function subscribeTheme(callback: () => void) {
  telegram()?.onEvent('themeChanged', callback)
  return () => telegram()?.offEvent('themeChanged', callback)
}

function themeSnapshot(): string {
  return JSON.stringify(telegram()?.themeParams ?? {})
}

export function useTelegramTheme(): TelegramThemeParams {
  return JSON.parse(useSyncExternalStore(subscribeTheme, themeSnapshot, () => '{}')) as TelegramThemeParams
}

export function getInitData(): string {
  if (telegram()?.initData) return telegram()!.initData
  return import.meta.env.DEV ? (sessionStorage.getItem('tma:initData') ?? '') : ''
}

export function isTelegram(): boolean {
  return Boolean(telegram()?.initData)
}

export function useTelegramLifecycle() {
  useEffect(() => {
    telegram()?.ready()
    telegram()?.expand()
  }, [])
}

export function useTelegramBack(visible: boolean, onBack: () => void) {
  useEffect(() => {
    const button = telegram()?.BackButton
    if (!button) return
    button.onClick(onBack)
    if (visible) button.show()
    else button.hide()
    return () => {
      button.offClick(onBack)
      button.hide()
    }
  }, [onBack, visible])
}

export function notify(type: 'error' | 'success' | 'warning') {
  telegram()?.HapticFeedback?.notificationOccurred(type)
}

export function openTelegramLink(url: string) {
  if (telegram()?.openTelegramLink) telegram()!.openTelegramLink!(url)
  else window.open(url, '_blank', 'noopener,noreferrer')
}

export function closeTelegram() {
  telegram()?.close()
}
