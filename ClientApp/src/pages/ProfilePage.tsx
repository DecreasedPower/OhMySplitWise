import SaveRounded from '@mui/icons-material/SaveRounded'
import { Avatar, Button, Card, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { refreshAfterConflict } from '../api/conflicts'
import { endpoints } from '../api/endpoints'
import { useIdempotencyKeyStore, type IdempotentSubmission } from '../api/idempotency'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import { notify } from '../platform/telegram'

export function ProfilePage() {
  const query = useQuery({ queryKey: ['profile'], queryFn: endpoints.profile })
  const client = useQueryClient()
  const keys = useIdempotencyKeyStore()
  const [details, setDetails] = useState('')
  const initialized = useRef(false)
  useEffect(() => {
    if (query.data && !initialized.current) {
      initialized.current = true
      setDetails(query.data.paymentDetails ?? '')
    }
  }, [query.data])
  const save = useMutation({
    mutationFn: ({ command, key }: IdempotentSubmission<{ paymentDetails: string | null; version: number | string }>) => endpoints.updateProfile(command.paymentDetails, { idempotencyKey: key, version: command.version }),
    onSuccess: async (profile, { key }) => {
      keys.settle(key)
      client.setQueryData(['profile'], profile)
      await Promise.all([
        client.invalidateQueries({ queryKey: ['participants'] }),
        client.invalidateQueries({ queryKey: ['balances'] }),
      ])
      notify('success')
    },
    onError: async (error, { key }) => { keys.settle(key, error); await refreshAfterConflict(error, client, [['profile']]); notify('error') },
  })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Профиль"><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  return <Page title="Профиль" eyebrow="Ваши данные">
    {query.data && <Card sx={{ p: 2.5, mb: 2.5 }}><Stack direction="row" spacing={2} alignItems="center"><Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.main' }}>{query.data.displayName.slice(0, 1).toUpperCase()}</Avatar><Stack><Typography variant="h3">{query.data.displayName}</Typography>{query.data.username && <Typography color="text.secondary">@{query.data.username}</Typography>}</Stack></Stack></Card>}
    <Stack spacing={2}><TextField label="Реквизиты для переводов" value={details} onChange={(event) => setDetails(event.target.value)} multiline minRows={4} inputProps={{ maxLength: 500 }} helperText="Их увидят участники, которым нужно перевести вам деньги." /><FormError message={save.error?.message} /><Button variant="contained" startIcon={<SaveRounded />} disabled={details.length > 500 || save.isPending} onClick={() => { if (!save.isPending) { const paymentDetails = details.trim() || null; save.mutate(keys.bind({ paymentDetails }, { paymentDetails, version: query.data.version })) } }}>{save.isPending ? 'Сохраняем…' : 'Сохранить'}</Button></Stack>
  </Page>
}
