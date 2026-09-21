import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded'
import { Alert, Button, Checkbox, Chip, FormControl, FormControlLabel, FormHelperText, InputLabel, MenuItem, Radio, RadioGroup, Select, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { refreshAfterConflict } from '../api/conflicts'
import { endpoints } from '../api/endpoints'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import { validateExpenseDraft, type ExpenseDraft } from '../domain/forms'
import { kopecksToInput, parseMoney, splitEqually } from '../domain/money'
import type { ExpenseInput, Participant } from '../domain/types'
import { notify } from '../platform/telegram'

const emptyDraft: ExpenseDraft = { description: '', amount: '', payerId: '', participantIds: [], splitMode: 'equal', manualAmounts: {} }
type ExpenseCommand = { groupId: string; input: ExpenseInput; revision: number | string } & (
  { expenseId: string; version: number | string } | { expenseId?: never; version?: never }
)

export function ExpenseFormPage() {
  const { groupId = '', expenseId } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const keys = useIdempotencyKeyStore()
  const [draft, setDraft] = useState<ExpenseDraft>(emptyDraft)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const initializedExpense = useRef<string | undefined>(undefined)
  const participants = useQuery({ queryKey: ['participants', groupId], queryFn: () => endpoints.participants(groupId) })
  const group = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const expense = useQuery({ queryKey: ['expense', groupId, expenseId], queryFn: () => endpoints.expense(groupId, expenseId!), enabled: Boolean(expenseId) })

  useEffect(() => {
    if (!expense.data?.shares || initializedExpense.current === expense.data.id) return
    initializedExpense.current = expense.data.id
    setDraft({
      description: expense.data.description,
      amount: kopecksToInput(expense.data.amountKopecks),
      payerId: expense.data.payerId,
      participantIds: expense.data.shares.map((share) => share.participantId),
      splitMode: 'manual',
      manualAmounts: Object.fromEntries(expense.data.shares.map((share) => [share.participantId, kopecksToInput(share.amountKopecks)])),
    })
  }, [expense.data])

  useEffect(() => {
    if (!participants.data) return
    const activeIds = new Set(participants.data.map((participant) => participant.id))
    setDraft((current) => {
      const participantIds = current.participantIds.filter((id) => activeIds.has(id))
      const payerId = activeIds.has(current.payerId) ? current.payerId : ''
      if (payerId === current.payerId && participantIds.length === current.participantIds.length) return current
      return {
        ...current,
        payerId,
        participantIds,
        manualAmounts: Object.fromEntries(Object.entries(current.manualAmounts).filter(([id]) => activeIds.has(id))),
      }
    })
  }, [participants.data])

  const save = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<ExpenseCommand>) => command.expenseId
      ? endpoints.updateExpense(command.groupId, command.expenseId, command.input, { idempotencyKey: key, version: command.version, groupRevision: command.revision })
      : endpoints.createExpense(command.groupId, command.input, { idempotencyKey: key, groupRevision: command.revision }),
    onSuccess: async (result, { key }) => {
      keys.settle(key)
      client.setQueryData(['expense', groupId, result.id], result)
      await Promise.all([
        client.invalidateQueries({ queryKey: ['expenses', groupId] }),
        client.invalidateQueries({ queryKey: ['group', groupId] }),
        client.invalidateQueries({ queryKey: ['groups'] }),
        client.invalidateQueries({ queryKey: ['balances', groupId] }),
      ])
      notify('success')
      navigate(`/groups/${groupId}/expenses/${result.id}`, { replace: true })
    },
    onError: async (error, { key }) => {
      keys.settle(key, error)
      await refreshAfterConflict(error, client, expenseId
        ? [['expense', groupId, expenseId], ['group', groupId], ['participants', groupId]]
        : [['group', groupId], ['participants', groupId]])
      notify('error')
    },
  })
  if (participants.isPending || group.isPending || (expenseId && expense.isPending)) return <PageLoader />
  if (participants.isError || group.isError || (expenseId && expense.isError)) return <Page title={expenseId ? 'Изменить покупку' : 'Новая покупка'}><ErrorState error={participants.error ?? group.error ?? expense.error} retry={() => { participants.refetch(); group.refetch(); if (expenseId) expense.refetch() }} /></Page>

  function submit(event: React.FormEvent) {
    event.preventDefault()
    if (save.isPending) return
    const nextErrors = validateExpenseDraft(draft)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) { notify('warning'); return }
    const amountKopecks = parseMoney(draft.amount)!
    const shares = draft.splitMode === 'equal'
      ? splitEqually(amountKopecks, draft.participantIds)
      : draft.participantIds.map((participantId) => ({ participantId, amountKopecks: parseMoney(draft.manualAmounts[participantId])! }))
    const input = { description: draft.description.trim(), amountKopecks, payerId: draft.payerId, shares }
    const intent = { groupId, expenseId, input }
    if (!group.data) return
    if (expenseId) {
      if (!expense.data) return
      save.mutate(keys.bind(intent, { groupId, expenseId, input, version: expense.data.version, revision: group.data.revision }))
    } else {
      save.mutate(keys.bind(intent, { groupId, input, revision: group.data.revision }))
    }
  }

  const list = participants.data ?? []
  return <Page title={expenseId ? 'Изменить покупку' : 'Новая покупка'} eyebrow="Расход">
    <Stack component="form" spacing={2.5} onSubmit={submit}>
      <TextField label="Что купили?" value={draft.description} onChange={(event) => setDraft({ ...draft, description: event.target.value })} error={Boolean(errors.description)} helperText={errors.description} inputProps={{ maxLength: 200 }} />
      <TextField label="Сумма" value={draft.amount} onChange={(event) => setDraft({ ...draft, amount: event.target.value })} error={Boolean(errors.amount)} helperText={errors.amount ?? 'Рубли и копейки'} inputMode="decimal" placeholder="1250,50" />
      <FormControl variant="filled" error={Boolean(errors.payerId)}><InputLabel id="expense-payer-label">Кто оплатил?</InputLabel><Select labelId="expense-payer-label" value={draft.payerId} onChange={(event) => setDraft({ ...draft, payerId: event.target.value })}>{list.map((person) => <MenuItem key={person.id} value={person.id}>{person.displayName}</MenuItem>)}</Select><FormHelperText>{errors.payerId}</FormHelperText></FormControl>
      <Stack spacing={1}>
        <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography component="h2" id="expense-participants-label" variant="h3">Для кого?</Typography><Chip component="button" type="button" label="Выбрать всех" onClick={() => setDraft({ ...draft, participantIds: list.map((person) => person.id) })} /></Stack>
        <Stack role="group" aria-labelledby="expense-participants-label">{list.map((person) => <ParticipantCheck key={person.id} person={person} checked={draft.participantIds.includes(person.id)} onChange={() => setDraft({ ...draft, participantIds: draft.participantIds.includes(person.id) ? draft.participantIds.filter((id) => id !== person.id) : [...draft.participantIds, person.id] })} />)}</Stack>
        {errors.participants && <FormHelperText error>{errors.participants}</FormHelperText>}
      </Stack>
      <FormControl><Typography component="h2" id="expense-split-label" variant="h3">Как разделить?</Typography><RadioGroup aria-labelledby="expense-split-label" row value={draft.splitMode} onChange={(event) => setDraft({ ...draft, splitMode: event.target.value as 'equal' | 'manual' })}><FormControlLabel value="equal" control={<Radio />} label="Поровну" /><FormControlLabel value="manual" control={<Radio />} label="Вручную" /></RadioGroup></FormControl>
      {draft.splitMode === 'manual' && <Stack spacing={1.5}>{draft.participantIds.map((id) => <TextField key={id} label={list.find((person) => person.id === id)?.displayName ?? id} value={draft.manualAmounts[id] ?? ''} onChange={(event) => setDraft({ ...draft, manualAmounts: { ...draft.manualAmounts, [id]: event.target.value } })} inputMode="decimal" />)}</Stack>}
      {errors.shares && <Alert severity="error">{errors.shares}</Alert>}
      <FormError message={save.error?.message} />
      <Button type="submit" variant="contained" startIcon={<CheckCircleRounded />} disabled={save.isPending}>{save.isPending ? 'Сохраняем…' : 'Сохранить покупку'}</Button>
    </Stack>
  </Page>
}

function ParticipantCheck({ person, checked, onChange }: { person: Participant; checked: boolean; onChange: () => void }) {
  return <FormControlLabel sx={{ m: 0, px: 1, minHeight: 48, borderRadius: 3, bgcolor: checked ? 'action.selected' : 'transparent' }} control={<Checkbox checked={checked} onChange={onChange} />} label={person.displayName} />
}
