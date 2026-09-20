import { alpha, createTheme, type ThemeOptions } from '@mui/material/styles'

function safeColor(value: string | undefined, fallback: string) {
  return value && /^#[0-9a-f]{6}$/i.test(value) ? value : fallback
}

export function createAppTheme(params: TelegramThemeParams, scheme: 'light' | 'dark' = 'light') {
  const dark = scheme === 'dark'
  const background = safeColor(params.bg_color, dark ? '#101412' : '#f6f7f2')
  const paper = safeColor(params.section_bg_color ?? params.secondary_bg_color, dark ? '#1a211e' : '#ffffff')
  const primary = safeColor(params.button_color ?? params.accent_text_color, dark ? '#7bdcb5' : '#176b52')
  const text = safeColor(params.text_color, dark ? '#e4e9e5' : '#1a1c1a')
  const secondaryText = safeColor(params.subtitle_text_color ?? params.hint_color, dark ? '#a8b2ac' : '#66706a')

  const options: ThemeOptions = {
    palette: {
      mode: dark ? 'dark' : 'light',
      primary: { main: primary, contrastText: safeColor(params.button_text_color, dark ? '#003828' : '#ffffff') },
      error: { main: safeColor(params.destructive_text_color, '#ba1a1a') },
      background: { default: background, paper },
      text: { primary: text, secondary: secondaryText },
      divider: safeColor(params.section_separator_color, alpha(text, 0.12)),
    },
    shape: { borderRadius: 18 },
    typography: {
      fontFamily: 'Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
      h1: { fontSize: '2rem', lineHeight: 1.1, fontWeight: 750, letterSpacing: '-0.04em' },
      h2: { fontSize: '1.5rem', lineHeight: 1.2, fontWeight: 720, letterSpacing: '-0.025em' },
      h3: { fontSize: '1.15rem', lineHeight: 1.25, fontWeight: 700 },
      button: { textTransform: 'none', fontWeight: 700 },
    },
    components: {
      MuiCssBaseline: { styleOverrides: { body: { overscrollBehavior: 'none' }, '#root': { minHeight: '100dvh' }, '*': { boxSizing: 'border-box' } } },
      MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { minHeight: 48, borderRadius: 16 } } },
      MuiCard: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: `1px solid ${alpha(text, 0.08)}`, borderRadius: 24 } } },
      MuiTextField: { defaultProps: { variant: 'filled' } },
      MuiFilledInput: { styleOverrides: { root: { borderRadius: 16, overflow: 'hidden', backgroundColor: alpha(text, 0.055), '&:before, &:after': { display: 'none' } } } },
      MuiChip: { styleOverrides: { root: { borderRadius: 12, fontWeight: 650 } } },
    },
  }
  return createTheme(options)
}
