import ArrowBackRounded from '@mui/icons-material/ArrowBackRounded'
import { Box, IconButton, Stack, Typography } from '@mui/material'
import { useCallback } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useTelegramBack } from '../platform/telegram'

export function Page({ title, eyebrow, action, children, back = true }: {
  title: string; eyebrow?: string; action?: React.ReactNode; children: React.ReactNode; back?: boolean
}) {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const goBack = useCallback(() => {
    if ((window.history.state?.idx ?? 0) > 0) navigate(-1)
    else navigate(parentPath(pathname), { replace: true })
  }, [navigate, pathname])
  useTelegramBack(back, goBack)
  return (
    <Box sx={{ px: { xs: 2, sm: 3 }, pt: 'max(18px, env(safe-area-inset-top))', pb: 'calc(32px + env(safe-area-inset-bottom))', maxWidth: 760, mx: 'auto' }}>
      <Stack direction="row" alignItems="center" spacing={1} sx={{ minHeight: 48, mb: 2 }}>
        {back && <IconButton aria-label="Назад" onClick={goBack} edge="start"><ArrowBackRounded /></IconButton>}
        <Box sx={{ minWidth: 0, flex: 1 }}>
          {eyebrow && <Typography variant="caption" color="primary" fontWeight={800} sx={{ textTransform: 'uppercase', letterSpacing: '.09em' }}>{eyebrow}</Typography>}
          <Typography component="h1" variant="h2" noWrap>{title}</Typography>
        </Box>
        {action}
      </Stack>
      {children}
    </Box>
  )
}

function parentPath(pathname: string) {
  const parts = pathname.split('/').filter(Boolean)
  if (parts[0] !== 'groups' || parts.length <= 2) return '/'
  if (parts[2] === 'expenses') {
    if (parts[4] === 'edit') return `/groups/${parts[1]}/expenses/${parts[3]}`
    if (parts.length === 4 && parts[3] !== 'new') return `/groups/${parts[1]}/expenses`
    if (parts.length === 4) return `/groups/${parts[1]}/expenses`
  }
  return `/groups/${parts[1]}`
}
