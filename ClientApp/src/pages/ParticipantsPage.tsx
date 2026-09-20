import AddRounded from '@mui/icons-material/AddRounded'
import EditRounded from '@mui/icons-material/EditRounded'
import PersonRounded from '@mui/icons-material/PersonRounded'
import { Avatar, Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import type { Participant } from '../domain/types'

export function ParticipantsPage() {
  const { groupId = '' } = useParams()
  const client = useQueryClient()
  const [editing, setEditing] = useState<Participant | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Participant | null>(null)
  const query = useQuery({ queryKey: ['participants', groupId], queryFn: () => endpoints.participants(groupId) })
  const group = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const remove = useMutation({
    mutationFn: (id: string) => endpoints.deleteParticipant(groupId, id),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ['participants', groupId] })
      client.invalidateQueries({ queryKey: ['group', groupId] })
      client.invalidateQueries({ queryKey: ['groups'] })
      client.invalidateQueries({ queryKey: ['balances', groupId] })
      client.invalidateQueries({ queryKey: ['expenses', groupId] })
      setDeleting(null)
    },
  })
  if (query.isPending || group.isPending) return <PageLoader />
  if (query.isError || group.isError) return <Page title="Участники"><ErrorState error={query.error ?? group.error} retry={() => { query.refetch(); group.refetch() }} /></Page>
  return <Page title="Участники" eyebrow={`${query.data.length} в группе`} action={group.data.type === 'standalone' && <IconButton aria-label="Добавить участника" onClick={() => setEditing('new')}><AddRounded /></IconButton>}>
    <Stack spacing={1.25}>{query.data.map((person) => <Card key={person.id} sx={{ p: 2 }}><Stack direction="row" spacing={1.5} alignItems="center">
      <Avatar><PersonRounded /></Avatar><Box sx={{ flex: 1, minWidth: 0 }}><Typography fontWeight={750}>{person.displayName}{person.isCurrentUser ? ' (вы)' : ''}</Typography>{person.paymentDetails && <Typography variant="body2" color="text.secondary" noWrap>{person.paymentDetails}</Typography>}</Box>
      {person.canEdit && <IconButton aria-label={`Изменить ${person.displayName}`} onClick={() => setEditing(person)}><EditRounded /></IconButton>}
    </Stack></Card>)}</Stack>
    {group.data.type === 'standalone' && <Button fullWidth startIcon={<AddRounded />} onClick={() => setEditing('new')} sx={{ mt: 2 }}>Добавить участника</Button>}
    <ParticipantDialog groupId={groupId} value={editing} close={() => setEditing(null)} onSaved={() => { client.invalidateQueries({ queryKey: ['participants', groupId] }); client.invalidateQueries({ queryKey: ['group', groupId] }); client.invalidateQueries({ queryKey: ['groups'] }) }} onDelete={(person) => { remove.reset(); setEditing(null); setDeleting(person) }} />
    <Dialog open={Boolean(deleting)} onClose={() => { if (!remove.isPending) setDeleting(null) }}>
      <DialogTitle>Удалить участника?</DialogTitle>
      <DialogContent><Typography color="text.secondary">{deleting?.displayName} будет удалён из группы. Это действие нельзя отменить.</Typography><FormError message={remove.error?.message} /></DialogContent>
      <DialogActions><Button onClick={() => setDeleting(null)} disabled={remove.isPending}>Отмена</Button><Button color="error" disabled={remove.isPending} onClick={() => { if (deleting && !remove.isPending) remove.mutate(deleting.id) }}>{remove.isPending ? 'Удаляем…' : 'Удалить'}</Button></DialogActions>
    </Dialog>
  </Page>
}

function ParticipantDialog({ groupId, value, close, onSaved, onDelete }: { groupId: string; value: Participant | 'new' | null; close: () => void; onSaved: () => void; onDelete: (person: Participant) => void }) {
  const initialName = value && value !== 'new' ? value.displayName : ''
  const initialDetails = value && value !== 'new' ? (value.paymentDetails ?? '') : ''
  const [name, setName] = useState(initialName)
  const [details, setDetails] = useState(initialDetails)
  const mutation = useMutation({
    mutationFn: () => value === 'new' ? endpoints.addParticipant(groupId, { displayName: name.trim(), paymentDetails: details.trim() || null }) : endpoints.updateParticipant(groupId, value!.id, { displayName: name.trim(), paymentDetails: details.trim() || null }),
    onSuccess: () => { onSaved(); close() },
  })
  // Remounting by key in the caller isn't available, so reset fields as a dialog enters.
  const open = value !== null
  function handleEnter() { setName(initialName); setDetails(initialDetails) }
  const valid = name.trim().length > 0 && name.trim().length <= 100 && details.length <= 500
  return <Dialog open={open} onClose={close} TransitionProps={{ onEnter: handleEnter }} fullWidth>
    <DialogTitle>{value === 'new' ? 'Новый участник' : 'Изменить участника'}</DialogTitle>
    <DialogContent><Stack spacing={2} sx={{ pt: 1 }}><TextField autoFocus label="Имя" value={name} onChange={(event) => setName(event.target.value)} inputProps={{ maxLength: 100 }} /><TextField label="Реквизиты" value={details} onChange={(event) => setDetails(event.target.value)} multiline minRows={2} inputProps={{ maxLength: 500 }} /><FormError message={mutation.error?.message} /></Stack></DialogContent>
    <DialogActions>{value !== 'new' && <Button color="error" disabled={mutation.isPending} onClick={() => onDelete(value!)}>Удалить</Button>}<Box sx={{ flex: 1 }} /><Button onClick={close} disabled={mutation.isPending}>Отмена</Button><Button variant="contained" disabled={!valid || mutation.isPending} onClick={() => { if (!mutation.isPending) mutation.mutate() }}>{mutation.isPending ? 'Сохраняем…' : 'Сохранить'}</Button></DialogActions>
  </Dialog>
}
