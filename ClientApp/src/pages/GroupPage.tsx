import AccountBalanceWalletRounded from '@mui/icons-material/AccountBalanceWalletRounded'
import AddRounded from '@mui/icons-material/AddRounded'
import DeleteOutlineRounded from '@mui/icons-material/DeleteOutlineRounded'
import GroupRounded from '@mui/icons-material/GroupRounded'
import LogoutRounded from '@mui/icons-material/LogoutRounded'
import ReceiptLongRounded from '@mui/icons-material/ReceiptLongRounded'
import ShareRounded from '@mui/icons-material/ShareRounded'
import { Button, Card, CardActionArea, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { ErrorState, FormError, PageLoader } from '../components/AsyncState'
import { Money } from '../components/Money'
import { Page } from '../components/Page'

export function GroupPage() {
  const { groupId = '' } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const [confirm, setConfirm] = useState(false)
  const query = useQuery({ queryKey: ['group', groupId], queryFn: () => endpoints.group(groupId) })
  const remove = useMutation({
    mutationFn: () => query.data?.isOwner ? endpoints.deleteGroup(groupId) : endpoints.leaveGroup(groupId),
    onSuccess: () => { client.removeQueries({ queryKey: ['group', groupId] }); client.invalidateQueries({ queryKey: ['groups'] }); navigate('/', { replace: true }) },
  })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Группа"><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  const group = query.data
  const isOwner = group.isOwner
  return (
    <Page title={group.name} eyebrow={group.type === 'collective' ? 'Общая группа' : 'Личная группа'}>
      <Card sx={{ p: 2.5, mb: 2.5, color: 'primary.contrastText', bgcolor: 'primary.main', backgroundImage: 'radial-gradient(circle at 90% 0%, rgba(255,255,255,.22), transparent 45%)' }}>
        <Typography variant="body2" sx={{ opacity: .8 }}>Ваш текущий баланс</Typography>
        <Money value={group.myBalanceKopecks} signed variant="h1" color="inherit" sx={{ mt: .5 }} />
        <Stack direction="row" spacing={1} sx={{ mt: 2 }}><Chip label={`${group.participantCount} участника`} sx={{ bgcolor: 'rgba(255,255,255,.16)', color: 'inherit' }} /><Chip label={group.type === 'collective' ? 'Вместе' : 'Самостоятельно'} sx={{ bgcolor: 'rgba(255,255,255,.16)', color: 'inherit' }} /></Stack>
      </Card>
      <Button component={RouterLink} to={`/groups/${groupId}/expenses/new`} fullWidth variant="contained" startIcon={<AddRounded />} sx={{ mb: 2.5 }}>Добавить покупку</Button>
      <Grid container spacing={1.5}>
        <ActionCard to={`/groups/${groupId}/expenses`} icon={<ReceiptLongRounded />} title="Покупки" />
        <ActionCard to={`/groups/${groupId}/balances`} icon={<AccountBalanceWalletRounded />} title="Баланс" />
        <ActionCard to={`/groups/${groupId}/participants`} icon={<GroupRounded />} title="Участники" />
        {group.type === 'collective' && <ActionCard to={`/groups/${groupId}/invite`} icon={<ShareRounded />} title="Пригласить" />}
      </Grid>
      <Button color="error" variant="text" startIcon={isOwner ? <DeleteOutlineRounded /> : <LogoutRounded />} onClick={() => setConfirm(true)} sx={{ mt: 4 }}>
        {isOwner ? 'Удалить группу' : 'Выйти из группы'}
      </Button>
      <Dialog open={confirm} onClose={() => { if (!remove.isPending) setConfirm(false) }}>
        <DialogTitle>{isOwner ? 'Удалить группу?' : 'Выйти из группы?'}</DialogTitle>
        <DialogContent><Typography color="text.secondary">{isOwner ? 'Группа исчезнет у всех участников. Это действие нельзя отменить.' : 'Выйти можно только с нулевым балансом.'}</Typography><FormError message={remove.error?.message} /></DialogContent>
        <DialogActions><Button onClick={() => setConfirm(false)} disabled={remove.isPending}>Отмена</Button><Button color="error" onClick={() => { if (!remove.isPending) remove.mutate() }} disabled={remove.isPending}>{remove.isPending ? 'Подождите…' : isOwner ? 'Удалить' : 'Выйти'}</Button></DialogActions>
      </Dialog>
    </Page>
  )
}

function ActionCard({ to, icon, title }: { to: string; icon: React.ReactNode; title: string }) {
  return <Grid size={{ xs: 6 }}><Card sx={{ height: '100%' }}><CardActionArea component={RouterLink} to={to} sx={{ p: 2, minHeight: 108 }}>
    <Stack spacing={1.5}>{icon}<Typography fontWeight={750}>{title}</Typography></Stack>
  </CardActionArea></Card></Grid>
}
