/// <reference types="vite/client" />

interface TelegramThemeParams {
  bg_color?: string
  text_color?: string
  hint_color?: string
  link_color?: string
  button_color?: string
  button_text_color?: string
  secondary_bg_color?: string
  header_bg_color?: string
  accent_text_color?: string
  destructive_text_color?: string
  section_bg_color?: string
  section_separator_color?: string
  subtitle_text_color?: string
}

interface TelegramWebApp {
  initData: string
  colorScheme: 'light' | 'dark'
  themeParams: TelegramThemeParams
  ready(): void
  expand(): void
  close(): void
  onEvent(event: 'themeChanged', handler: () => void): void
  offEvent(event: 'themeChanged', handler: () => void): void
  BackButton: {
    show(): void
    hide(): void
    onClick(handler: () => void): void
    offClick(handler: () => void): void
  }
  HapticFeedback?: {
    notificationOccurred(type: 'error' | 'success' | 'warning'): void
  }
  openTelegramLink?(url: string): void
}

interface Window {
  Telegram?: { WebApp?: TelegramWebApp }
}
