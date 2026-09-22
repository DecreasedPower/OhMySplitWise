import AddRounded from '@mui/icons-material/AddRounded'
import Groups2Rounded from '@mui/icons-material/Groups2Rounded'
import PersonRounded from '@mui/icons-material/PersonRounded'
import SettingsRounded from '@mui/icons-material/SettingsRounded'
import { Avatar, Box, Card, CardActionArea, Chip, Fab, IconButton, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { ErrorState, PageLoader } from '../components/AsyncState'
import { Money } from '../components/Money'
import { Page } from '../components/Page'

export function GroupsPage() {
  const query = useQuery({ queryKey: ['groups'], queryFn: endpoints.groups })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Мои группы" back={false}><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  return (
    <Page title="Мои группы" back={false} action={<IconButton component={RouterLink} to="/profile" aria-label="Профиль"><SettingsRounded /></IconButton>}>
      <Stack spacing={1.5}>
        {query.data.length === 0 ? (
          <Box sx={{ py: 10, textAlign: 'center' }}>
            <Avatar sx={{ width: 64, height: 64, mx: 'auto', mb: 2, bgcolor: 'primary.main' }}><Groups2Rounded /></Avatar>
            <Typography variant="h3">Начните с первой группы</Typography>
            <Typography color="text.secondary" sx={{ mt: 1 }}>Добавляйте покупки, делите суммы и закрывайте долги.</Typography>
          </Box>
        ) : query.data.map((group) => (
          <Card key={group.id}>
            <CardActionArea component={RouterLink} to={`/groups/${group.id}`} sx={{ p: 2.25 }}>
              <Stack direction="row" spacing={2} alignItems="center">
                <Avatar sx={{ bgcolor: group.type === 'collective' ? 'primary.main' : 'secondary.main' }}>
                  {group.type === 'collective' ? <Groups2Rounded /> : <PersonRounded />}
                </Avatar>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="h3" noWrap>{group.name}</Typography>
                  <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: .75 }}>
                    <Chip size="small" label={`${group.type === 'collective' ? 'Общая' : 'Личная'} · ${group.participantCount} участн.`} />
                    <Typography variant="caption" color="text.secondary">Покупки: <Money component="span" variant="caption" value={group.totalExpensesKopecks} /></Typography>
                  </Stack>
                </Box>
                <Box sx={{ textAlign: 'right' }}>
                  <Typography variant="caption" color="text.secondary">Ваш баланс</Typography>
                  <Money value={group.myBalanceKopecks} signed fontWeight={750} />
                </Box>
              </Stack>
            </CardActionArea>
          </Card>
        ))}
      </Stack>
      <Fab component={RouterLink} to="/groups/new" color="primary" aria-label="Создать группу" sx={{ position: 'fixed', right: 20, bottom: 'calc(20px + env(safe-area-inset-bottom))' }}>
        <AddRounded />
      </Fab>
    </Page>
  )
}
