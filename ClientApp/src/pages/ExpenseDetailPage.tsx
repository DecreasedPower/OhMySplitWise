import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded'
import EditRounded from '@mui/icons-material/EditRounded'
import { Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Money } from '../components/Money'
import { Page } from '../components/Page'

export function ExpenseDetailPage() {
  const { groupId = '', expenseId = '' } = useParams()
  const [confirm, setConfirm] = useState(false)
  const navigate = useNavigate()
  const client = useQueryClient()
  const keys = useIdempotencyKeyStore()
  const query = useQuery({ queryKey: ['expense', groupId, expenseId], queryFn: () => endpoints.expense(groupId, expenseId) })
  const group = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const remove = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; expenseId: string; version?: number | string; revision?: number | string }>) => endpoints.deleteExpense(command.groupId, command.expenseId, { idempotencyKey: key, version: command.version, groupRevision: command.revision }),
    onSuccess: async (_, { key }) => {
      keys.settle(key)
      client.removeQueries({ queryKey: ['expense', groupId, expenseId] })
      await Promise.all([
        client.invalidateQueries({ queryKey: ['expenses', groupId] }),
        client.invalidateQueries({ queryKey: ['balances', groupId] }),
        client.invalidateQueries({ queryKey: ['group', groupId] }),
        client.invalidateQueries({ queryKey: ['groups'] }),
      ])
      navigate(`/groups/${groupId}/expenses`, { replace: true })
    },
    onError: (error, { key }) => keys.settle(key, error),
  })
  if (query.isPending || group.isPending) return <PageLoader />
  if (query.isError || group.isError) return <Page title="Покупка"><ErrorState error={query.error ?? group.error} retry={() => { query.refetch(); group.refetch() }} /></Page>
  const expense = query.data
  return <Page title={expense.description} eyebrow={new Intl.DateTimeFormat('ru-RU', { dateStyle: 'long' }).format(new Date(expense.createdAt))}>
    <Card sx={{ p: 2.5 }}><Typography color="text.secondary">Сумма</Typography><Money value={expense.amountKopecks} variant="h1" sx={{ mt: .5 }} /><Divider sx={{ my: 2 }} /><Typography color="text.secondary">Оплатил</Typography><Typography variant="h3" sx={{ mt: .5 }}>{expense.payerName}</Typography></Card>
    <Typography component="h2" variant="h3" sx={{ mt: 3, mb: 1.5 }}>Доли участников</Typography>
    <Card>{expense.shares?.map((share, index) => <Box key={share.participantId}><Stack direction="row" justifyContent="space-between" sx={{ p: 2 }}><Typography>{share.participantName}</Typography><Money value={share.amountKopecks} fontWeight={700} /></Stack>{index < (expense.shares?.length ?? 0) - 1 && <Divider />}</Box>)}</Card>
    {expense.canEdit && <Stack direction="row" spacing={1.5} sx={{ mt: 3 }}><Button component={RouterLink} to={`/groups/${groupId}/expenses/${expenseId}/edit`} variant="contained" startIcon={<EditRounded />} sx={{ flex: 1 }}>Изменить</Button><Button color="error" variant="outlined" startIcon={<DeleteOutlineRounded />} onClick={() => setConfirm(true)}>Удалить</Button></Stack>}
    <Dialog open={confirm} onClose={() => { if (!remove.isPending) setConfirm(false) }}><DialogTitle>Удалить покупку?</DialogTitle><DialogContent><Typography color="text.secondary">Баланс группы будет пересчитан.</Typography><FormError message={remove.error?.message} /></DialogContent><DialogActions><Button onClick={() => setConfirm(false)} disabled={remove.isPending}>Отмена</Button><Button color="error" onClick={() => { if (!remove.isPending) remove.mutate(keys.bind({ groupId, expenseId }, { groupId, expenseId, version: expense.version, revision: group.data.revision })) }} disabled={remove.isPending}>{remove.isPending ? 'Удаляем…' : 'Удалить'}</Button></DialogActions></Dialog>
  </Page>
}
