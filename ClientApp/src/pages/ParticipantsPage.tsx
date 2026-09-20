import AddRounded from '@mui/icons-material/AddRounded'
import EditRounded from '@mui/icons-material/EditRounded'
import PersonRounded from '@mui/icons-material/PersonRounded'
import { Avatar, Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import type { Participant } from '../domain/types'

export function ParticipantsPage() {
  const { groupId = '' } = useParams()
  const client = useQueryClient()
  const removeKeys = useIdempotencyKeyStore()
  const [editing, setEditing] = useState<Participant | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Participant | null>(null)
  const query = useQuery({ queryKey: ['participants', groupId], queryFn: () => endpoints.participants(groupId) })
  const group = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const remove = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; person: Participant; revision?: number | string }>) => endpoints.deleteParticipant(command.groupId, command.person.id, { idempotencyKey: key, version: command.person.version, groupRevision: command.revision }),
    onSuccess: async (_, { key }) => {
      removeKeys.settle(key)
      await refreshParticipantProjections(client, groupId)
      setDeleting(null)
    },
    onError: (error, { key }) => removeKeys.settle(key, error),
  })
  if (query.isPending || group.isPending) return <PageLoader />
  if (query.isError || group.isError) return <Page title="Участники"><ErrorState error={query.error ?? group.error} retry={() => { query.refetch(); group.refetch() }} /></Page>
  return <Page title="Участники" eyebrow={`${query.data.length} в группе`} action={group.data.type === 'standalone' && <IconButton aria-label="Добавить участника" onClick={() => setEditing('new')}><AddRounded /></IconButton>}>
    <Stack spacing={1.25}>{query.data.map((person) => <Card key={person.id} sx={{ p: 2 }}><Stack direction="row" spacing={1.5} alignItems="center">
      <Avatar><PersonRounded /></Avatar><Box sx={{ flex: 1, minWidth: 0 }}><Typography fontWeight={750}>{person.displayName}{person.isCurrentUser ? ' (вы)' : ''}</Typography>{person.paymentDetails && <Typography variant="body2" color="text.secondary" noWrap>{person.paymentDetails}</Typography>}</Box>
      {person.canEdit && <IconButton aria-label={`Изменить ${person.displayName}`} onClick={() => setEditing(person)}><EditRounded /></IconButton>}
    </Stack></Card>)}</Stack>
    {group.data.type === 'standalone' && <Button fullWidth startIcon={<AddRounded />} onClick={() => setEditing('new')} sx={{ mt: 2 }}>Добавить участника</Button>}
    <ParticipantDialog groupId={groupId} groupRevision={group.data.revision} value={editing} close={() => setEditing(null)} onSaved={() => refreshParticipantProjections(client, groupId)} onDelete={(person) => { remove.reset(); setEditing(null); setDeleting(person) }} />
    <Dialog open={Boolean(deleting)} onClose={() => { if (!remove.isPending) setDeleting(null) }}>
      <DialogTitle>Удалить участника?</DialogTitle>
      <DialogContent><Typography color="text.secondary">{deleting?.displayName} будет удалён из группы. Это действие нельзя отменить.</Typography><FormError message={remove.error?.message} /></DialogContent>
      <DialogActions><Button onClick={() => setDeleting(null)} disabled={remove.isPending}>Отмена</Button><Button color="error" disabled={remove.isPending} onClick={() => { if (deleting && !remove.isPending) remove.mutate(removeKeys.bind({ groupId, participantId: deleting.id }, { groupId, person: deleting, revision: group.data.revision })) }}>{remove.isPending ? 'Удаляем…' : 'Удалить'}</Button></DialogActions>
    </Dialog>
  </Page>
}

function ParticipantDialog({ groupId, groupRevision, value, close, onSaved, onDelete }: { groupId: string; groupRevision: number | string; value: Participant | 'new' | null; close: () => void; onSaved: () => Promise<void>; onDelete: (person: Participant) => void }) {
  const initialName = value && value !== 'new' ? value.displayName : ''
  const initialDetails = value && value !== 'new' ? (value.paymentDetails ?? '') : ''
  const [name, setName] = useState(initialName)
  const [details, setDetails] = useState(initialDetails)
  const keys = useIdempotencyKeyStore()
  const mutation = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ groupId: string; input: { displayName: string; paymentDetails: string | null }; participant: Participant | 'new'; revision: number | string }>) => command.participant === 'new'
      ? endpoints.addParticipant(command.groupId, command.input, { idempotencyKey: key, groupRevision: command.revision })
      : endpoints.updateParticipant(command.groupId, command.participant.id, command.input, { idempotencyKey: key, version: command.participant.version, groupRevision: command.revision }),
    onSuccess: async (_, { key }) => { keys.settle(key); await onSaved(); close() },
    onError: (error, { key }) => keys.settle(key, error),
  })
  // Remounting by key in the caller isn't available, so reset fields as a dialog enters.
  const open = value !== null
  function handleEnter() { setName(initialName); setDetails(initialDetails) }
  const valid = name.trim().length > 0 && name.trim().length <= 100 && details.length <= 500
  return <Dialog open={open} onClose={close} TransitionProps={{ onEnter: handleEnter }} fullWidth>
    <DialogTitle>{value === 'new' ? 'Новый участник' : 'Изменить участника'}</DialogTitle>
    <DialogContent><Stack spacing={2} sx={{ pt: 1 }}><TextField autoFocus label="Имя" value={name} onChange={(event) => setName(event.target.value)} inputProps={{ maxLength: 100 }} /><TextField label="Реквизиты" value={details} onChange={(event) => setDetails(event.target.value)} multiline minRows={2} inputProps={{ maxLength: 500 }} /><FormError message={mutation.error?.message} /></Stack></DialogContent>
    <DialogActions>{value !== 'new' && <Button color="error" disabled={mutation.isPending} onClick={() => onDelete(value!)}>Удалить</Button>}<Box sx={{ flex: 1 }} /><Button onClick={close} disabled={mutation.isPending}>Отмена</Button><Button variant="contained" disabled={!valid || mutation.isPending} onClick={() => { if (!mutation.isPending && value) { const input = { displayName: name.trim(), paymentDetails: details.trim() || null }; const intent = { groupId, participantId: value === 'new' ? 'new' : value.id, input }; mutation.mutate(keys.bind(intent, { groupId, input, participant: value, revision: groupRevision })) } }}>{mutation.isPending ? 'Сохраняем…' : 'Сохранить'}</Button></DialogActions>
  </Dialog>
}

async function refreshParticipantProjections(client: ReturnType<typeof useQueryClient>, groupId: string) {
  await Promise.all([
    client.invalidateQueries({ queryKey: ['participants', groupId] }),
    client.invalidateQueries({ queryKey: ['group', groupId] }),
    client.invalidateQueries({ queryKey: ['groups'] }),
    client.invalidateQueries({ queryKey: ['balances', groupId] }),
    client.invalidateQueries({ queryKey: ['expenses', groupId] }),
    client.invalidateQueries({ queryKey: ['profile'] }),
  ])
}
