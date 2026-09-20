/* eslint-disable react-refresh/only-export-components */
import { CssBaseline, ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode, useEffect, useMemo } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { ApiError } from './api/client'
import { useTelegramTheme } from './platform/telegram'
import { createAppTheme } from './theme'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 },
    mutations: { retry: false },
  },
})

function Root() {
  const params = useTelegramTheme()
  const scheme = window.Telegram?.WebApp?.colorScheme ?? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
  const theme = useMemo(() => createAppTheme(params, scheme), [params, scheme])
  useEffect(() => {
    document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')?.setAttribute('content', theme.palette.background.default)
  }, [theme])
  return <ThemeProvider theme={theme}><CssBaseline /><QueryClientProvider client={queryClient}><BrowserRouter><App /></BrowserRouter></QueryClientProvider></ThemeProvider>
}

createRoot(document.getElementById('root')!).render(<StrictMode><Root /></StrictMode>)
