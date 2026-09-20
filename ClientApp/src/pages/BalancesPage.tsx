import CheckRounded from '@mui/icons-material/CheckRounded'
import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded'
import PaymentsRounded from '@mui/icons-material/PaymentsRounded'
import { Alert, Box, Button, Card, Chip, Divider, IconButton, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Money } from '../components/Money'
import { Page } from '../components/Page'
import type { PendingTransfer } from '../domain/types'
import { notify } from '../platform/telegram'

export function BalancesPage() {
  const { groupId = '' } = useParams()
  const client = useQueryClient()
  const paidKeys = useIdempotencyKeyStore()
  const resolveKeys = useIdempotencyKeyStore()
  const query = useQuery({ queryKey: ['balances', groupId], queryFn: () => endpoints.balances(groupId) })
  const refresh = () => Promise.all([
    client.invalidateQueries({ queryKey: ['balances', groupId] }),
    client.invalidateQueries({ queryKey: ['group', groupId] }),
    client.invalidateQueries({ queryKey: ['groups'] }),
  ])
  const paid = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; to: string; revision?: number | string }>) => endpoints.markPaid(command.groupId, command.to, { idempotencyKey: key, groupRevision: command.revision }),
    onSuccess: async (_, { key }) => { paidKeys.settle(key); await refresh(); notify('success') },
    onError: (error, { key }) => { paidKeys.settle(key, error); notify('error') },
  })
  const resolve = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; transfer: PendingTransfer; resolution: 'confirmed' | 'rejected'; revision?: number | string }>) => endpoints.resolveTransfer(command.groupId, command.transfer.id, command.resolution, { idempotencyKey: key, version: command.transfer.version, groupRevision: command.revision }),
    onSuccess: async (_, { key }) => { resolveKeys.settle(key); await refresh(); notify('success') },
    onError: (error, { key }) => { resolveKeys.settle(key, error); notify('error') },
  })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Баланс"><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  const data = query.data
  return <Page title="Баланс" eyebrow="Кто кому должен">
    <FormError message={paid.error?.message ?? resolve.error?.message} />
    <Card>{data.balances.map((balance, index) => <Box key={balance.participantId}><Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2 }}><Typography fontWeight={650}>{balance.participantName}</Typography><Money value={balance.amountKopecks} signed fontWeight={800} /></Stack>{index < data.balances.length - 1 && <Divider />}</Box>)}</Card>
    <Typography component="h2" variant="h3" sx={{ mt: 3, mb: 1.5 }}>Рекомендуемые переводы</Typography>
    {data.suggestions.length === 0 ? <Alert icon={<CheckRounded />} severity="success">Все расчёты закрыты</Alert> : <Stack spacing={1.25}>{data.suggestions.map((item) => <Card key={`${item.fromParticipantId}-${item.toParticipantId}`} sx={{ p: 2 }}>
      <Stack direction="row" spacing={1.5} alignItems="center"><PaymentsRounded color="primary" /><Box sx={{ flex: 1 }}><Typography><b>{item.fromName}</b> → <b>{item.toName}</b></Typography><Money value={item.amountKopecks} variant="h3" sx={{ mt: .5 }} /></Box>{item.pendingTransferId && <Chip label="Ожидает" />}</Stack>
      {item.paymentDetails && <Stack direction="row" alignItems="center" sx={{ mt: 1.5, p: 1.25, bgcolor: 'background.default', borderRadius: 3 }}><Typography variant="body2" sx={{ flex: 1, whiteSpace: 'pre-wrap' }}>{item.paymentDetails}</Typography><IconButton size="small" aria-label="Скопировать реквизиты" onClick={() => navigator.clipboard.writeText(item.paymentDetails!)}><ContentCopyRounded fontSize="small" /></IconButton></Stack>}
      {item.canMarkPaid && !item.pendingTransferId && <Button fullWidth variant="contained" disabled={paid.isPending || resolve.isPending} onClick={() => { if (!paid.isPending && !resolve.isPending) { const intent = { groupId, to: item.toParticipantId }; paid.mutate(paidKeys.bind(intent, { ...intent, revision: data.groupRevision })) } }} sx={{ mt: 1.5 }}>{paid.isPending && paid.variables?.command.to === item.toParticipantId ? 'Отправляем…' : 'Я перевёл'}</Button>}
    </Card>)}</Stack>}
    {data.pendingTransfers.length > 0 && <><Typography component="h2" variant="h3" sx={{ mt: 3, mb: 1.5 }}>Ожидают подтверждения</Typography><Stack spacing={1.25}>{data.pendingTransfers.map((transfer) => <Card key={transfer.id} sx={{ p: 2 }}><Typography><b>{transfer.fromName}</b> перевёл вам</Typography><Money value={transfer.amountKopecks} variant="h3" sx={{ my: 1 }} />{transfer.canResolve && <Stack direction="row" spacing={1}><Button variant="contained" disabled={paid.isPending || resolve.isPending} onClick={() => { if (!paid.isPending && !resolve.isPending) { const intent = { groupId, transferId: transfer.id, resolution: 'confirmed' as const }; resolve.mutate(resolveKeys.bind(intent, { groupId, transfer, resolution: intent.resolution, revision: data.groupRevision })) } }}>Получено</Button><Button color="error" disabled={paid.isPending || resolve.isPending} onClick={() => { if (!paid.isPending && !resolve.isPending) { const intent = { groupId, transferId: transfer.id, resolution: 'rejected' as const }; resolve.mutate(resolveKeys.bind(intent, { groupId, transfer, resolution: intent.resolution, revision: data.groupRevision })) } }}>Не получено</Button></Stack>}</Card>)}</Stack></>}
  </Page>
}
