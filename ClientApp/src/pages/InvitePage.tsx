import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded'
import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded'
import SendRounded from '@mui/icons-material/SendRounded'
import { Alert, Button, Card, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { refreshAfterConflict } from '../api/conflicts'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import type { Invitation } from '../domain/types'
import { notify, openTelegramLink } from '../platform/telegram'

export function InvitePage() {
  const { groupId = '' } = useParams()
  const client = useQueryClient()
  const createKeys = useIdempotencyKeyStore()
  const revokeKeys = useIdempotencyKeyStore()
  const [copied, setCopied] = useState(false)
  const [now, setNow] = useState(Date.now())
  const invitations = useQuery({ queryKey: ['invitations', groupId], queryFn: () => endpoints.invitations(groupId) })
  const group = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const create = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; revision: number | string }>) => endpoints.createInvitation(command.groupId, { idempotencyKey: key, groupRevision: command.revision }),
    onSuccess: async (invitation, { key }) => {
      createKeys.settle(key)
      client.setQueryData<Invitation[]>(['invitations', groupId], (current = []) => [...current.filter((item) => item.id !== invitation.id), invitation])
      await client.invalidateQueries({ queryKey: ['group', groupId] })
      setCopied(false)
      setNow(Date.now())
      notify('success')
    },
    onError: async (error, { key }) => { createKeys.settle(key, error); await refreshAfterConflict(error, client, [['invitations', groupId], ['group', groupId]]); notify('error') },
  })
  const revoke = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; invitation: Invitation; revision: number | string }>) => endpoints.revokeInvitation(command.groupId, command.invitation.id, { idempotencyKey: key, version: command.invitation.version, groupRevision: command.revision }),
    onSuccess: async (_, { command, key }) => {
      revokeKeys.settle(key)
      client.setQueryData<Invitation[]>(['invitations', groupId], (current = []) => current.map((item) => item.id === command.invitation.id ? { ...item, isActive: false } : item))
      await client.invalidateQueries({ queryKey: ['group', groupId] })
      notify('success')
    },
    onError: async (error, { key }) => { revokeKeys.settle(key, error); await refreshAfterConflict(error, client, [['invitations', groupId], ['group', groupId]]); notify('error') },
  })
  const activeInvitation = invitations.data?.find((item) => item.isActive)
  useEffect(() => {
    if (!activeInvitation) return
    const delay = new Date(activeInvitation.expiresAt).getTime() - now
    if (delay <= 0) return
    const timer = window.setTimeout(() => setNow(Date.now()), Math.min(delay + 1, 2_147_483_647))
    return () => window.clearTimeout(timer)
  }, [activeInvitation, now])
  if (invitations.isPending || group.isPending) return <PageLoader />
  if (invitations.isError || group.isError) return <Page title="Пригласить"><ErrorState error={invitations.error ?? group.error} retry={() => { invitations.refetch(); group.refetch() }} /></Page>

  const invitation = activeInvitation && new Date(activeInvitation.expiresAt).getTime() > now ? activeInvitation : undefined
  return <Page title="Пригласить" eyebrow="Общая группа">
    <Stack spacing={2.5} alignItems="stretch">
      <Card sx={{ p: 3, textAlign: 'center', backgroundImage: 'radial-gradient(circle at 50% 0%, rgba(23,107,82,.16), transparent 65%)' }}><Typography variant="h2">Расходы проще делить вместе</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>Отправьте ссылку участникам. После открытия бота они присоединятся к группе.</Typography></Card>
      {copied && <Alert severity="success">Ссылка скопирована</Alert>}
      {invitation ? <>
        <Button variant="contained" startIcon={<SendRounded />} onClick={() => openTelegramLink(invitation.telegramShareUrl)}>Отправить в Telegram</Button>
        <Button variant="outlined" startIcon={<ContentCopyRounded />} onClick={async () => { await navigator.clipboard.writeText(invitation.shareUrl); setCopied(true); notify('success') }}>Скопировать ссылку</Button>
        <Typography variant="caption" color="text.secondary" textAlign="center" sx={{ wordBreak: 'break-all' }}>{invitation.shareUrl}</Typography>
        <Typography variant="caption" color="text.secondary" textAlign="center">Действует до {new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(invitation.expiresAt))}</Typography>
        <Button color="error" startIcon={<DeleteOutlineRounded />} disabled={create.isPending || revoke.isPending} onClick={() => revoke.mutate(revokeKeys.bind({ groupId, invitationId: invitation.id }, { groupId, invitation, revision: group.data.revision }))}>{revoke.isPending ? 'Отзываем…' : 'Отозвать ссылку'}</Button>
      </> : <Button variant="contained" startIcon={<SendRounded />} disabled={create.isPending || revoke.isPending} onClick={() => create.mutate(createKeys.bind({ groupId, action: 'create' }, { groupId, revision: group.data.revision }))}>{create.isPending ? 'Создаём…' : 'Создать ссылку'}</Button>}
      <FormError message={create.error?.message ?? revoke.error?.message} />
    </Stack>
  </Page>
}
