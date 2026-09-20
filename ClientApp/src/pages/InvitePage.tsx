import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded'
import SendRounded from '@mui/icons-material/SendRounded'
import { Alert, Button, Card, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import { notify, openTelegramLink } from '../platform/telegram'

export function InvitePage() {
  const { groupId = '' } = useParams()
  const [copied, setCopied] = useState(false)
  const invite = useQuery({ queryKey: ['invitation', groupId], queryFn: () => endpoints.invitation(groupId), staleTime: Infinity, retry: false })
  if (invite.isPending) return <PageLoader />
  return <Page title="Пригласить" eyebrow="Общая группа">
    {invite.data && <Stack spacing={2.5} alignItems="stretch">
      <Card sx={{ p: 3, textAlign: 'center', backgroundImage: 'radial-gradient(circle at 50% 0%, rgba(23,107,82,.16), transparent 65%)' }}><Typography variant="h2">Расходы проще делить вместе</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>Отправьте ссылку участникам. После открытия бота они присоединятся к группе.</Typography></Card>
      {copied && <Alert severity="success">Ссылка скопирована</Alert>}
      <Button variant="contained" startIcon={<SendRounded />} onClick={() => openTelegramLink(invite.data!.telegramShareUrl)}>Отправить в Telegram</Button>
      <Button variant="outlined" startIcon={<ContentCopyRounded />} onClick={async () => { await navigator.clipboard.writeText(invite.data!.shareUrl); setCopied(true); notify('success') }}>Скопировать ссылку</Button>
      <Typography variant="caption" color="text.secondary" textAlign="center" sx={{ wordBreak: 'break-all' }}>{invite.data.shareUrl}</Typography>
    </Stack>}
    <FormError message={invite.error?.message} />
  </Page>
}
