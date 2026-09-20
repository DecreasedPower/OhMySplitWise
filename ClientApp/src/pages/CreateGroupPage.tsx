import Groups2Rounded from '@mui/icons-material/Groups2Rounded'
import PersonRounded from '@mui/icons-material/PersonRounded'
import { Button, Card, CardActionArea, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { FormError } from '../components/AsyncState'
import { Page } from '../components/Page'
import type { GroupType } from '../domain/types'
import { notify } from '../platform/telegram'

export function CreateGroupPage() {
  const [name, setName] = useState('')
  const [type, setType] = useState<GroupType>('collective')
  const navigate = useNavigate()
  const client = useQueryClient()
  const mutation = useMutation({
    mutationFn: () => endpoints.createGroup(name.trim(), type),
    onSuccess: (group) => { client.invalidateQueries({ queryKey: ['groups'] }); notify('success'); navigate(`/groups/${group.id}`, { replace: true }) },
    onError: () => notify('error'),
  })
  const valid = name.trim().length > 0 && name.trim().length <= 100
  return (
    <Page title="Новая группа" eyebrow="Один шаг">
      <Stack component="form" spacing={2.5} onSubmit={(event) => { event.preventDefault(); if (valid && !mutation.isPending) mutation.mutate() }}>
        <TextField autoFocus label="Название" value={name} onChange={(event) => setName(event.target.value)} inputProps={{ maxLength: 100 }} helperText={`${name.trim().length}/100`} error={name.length > 0 && !valid} />
        <Typography component="h2" id="group-type-label" variant="h3">Как будете вести расходы?</Typography>
        <Stack role="radiogroup" aria-labelledby="group-type-label" spacing={2.5}>
          <TypeCard active={type === 'collective'} icon={<Groups2Rounded />} title="Вместе" text="Каждый ведёт свои расходы и подтверждает переводы." onClick={() => setType('collective')} />
          <TypeCard active={type === 'standalone'} icon={<PersonRounded />} title="Самостоятельно" text="Вы управляете участниками и расчётами сами." onClick={() => setType('standalone')} />
        </Stack>
        <FormError message={mutation.error?.message} />
        <Button type="submit" variant="contained" size="large" disabled={!valid || mutation.isPending}>{mutation.isPending ? 'Создаём…' : 'Создать группу'}</Button>
      </Stack>
    </Page>
  )
}

function TypeCard({ active, icon, title, text, onClick }: { active: boolean; icon: React.ReactNode; title: string; text: string; onClick: () => void }) {
  return <Card sx={{ borderColor: active ? 'primary.main' : 'divider', borderWidth: active ? 2 : 1 }}>
    <CardActionArea role="radio" aria-checked={active} onClick={onClick} sx={{ p: 2.25 }}>
      <Stack direction="row" spacing={2} alignItems="center">
        {icon}<Stack><Typography fontWeight={750}>{title}</Typography><Typography variant="body2" color="text.secondary">{text}</Typography></Stack>
      </Stack>
    </CardActionArea>
  </Card>
}
