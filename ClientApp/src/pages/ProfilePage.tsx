import SaveRounded from '@mui/icons-material/SaveRounded'
import { Avatar, Button, Card, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { endpoints } from '../api/endpoints'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Page } from '../components/Page'
import { notify } from '../platform/telegram'

export function ProfilePage() {
  const query = useQuery({ queryKey: ['profile'], queryFn: endpoints.profile })
  const client = useQueryClient()
  const [details, setDetails] = useState('')
  useEffect(() => { if (query.data) setDetails(query.data.paymentDetails ?? '') }, [query.data])
  const save = useMutation({ mutationFn: () => endpoints.updateProfile(details.trim() || null), onSuccess: (profile) => { client.setQueryData(['profile'], profile); notify('success') }, onError: () => notify('error') })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Профиль"><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  return <Page title="Профиль" eyebrow="Ваши данные">
    {query.data && <Card sx={{ p: 2.5, mb: 2.5 }}><Stack direction="row" spacing={2} alignItems="center"><Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.main' }}>{query.data.displayName.slice(0, 1).toUpperCase()}</Avatar><Stack><Typography variant="h3">{query.data.displayName}</Typography>{query.data.username && <Typography color="text.secondary">@{query.data.username}</Typography>}</Stack></Stack></Card>}
    <Stack spacing={2}><TextField label="Реквизиты для переводов" value={details} onChange={(event) => setDetails(event.target.value)} multiline minRows={4} inputProps={{ maxLength: 500 }} helperText="Их увидят участники, которым нужно перевести вам деньги." /><FormError message={save.error?.message} /><Button variant="contained" startIcon={<SaveRounded />} disabled={details.length > 500 || save.isPending} onClick={() => { if (!save.isPending) save.mutate() }}>{save.isPending ? 'Сохраняем…' : 'Сохранить'}</Button></Stack>
  </Page>
}
