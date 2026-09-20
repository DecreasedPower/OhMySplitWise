import ErrorOutlineRounded from '@mui/icons-material/ErrorOutlineRounded'
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material'

export function PageLoader() {
  return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '45dvh' }}><CircularProgress size={32} /></Box>
}

export function ErrorState({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <Stack alignItems="center" spacing={2} sx={{ py: 8, textAlign: 'center' }}>
      <ErrorOutlineRounded color="error" sx={{ fontSize: 42 }} />
      <Typography variant="h3">Не удалось загрузить данные</Typography>
      <Typography color="text.secondary">{error instanceof Error ? error.message : 'Проверьте соединение и попробуйте снова.'}</Typography>
      {retry && <Button variant="contained" onClick={retry}>Повторить</Button>}
    </Stack>
  )
}

export function FormError({ message }: { message?: string }) {
  return message ? <Alert severity="error" sx={{ borderRadius: 3 }}>{message}</Alert> : null
}
