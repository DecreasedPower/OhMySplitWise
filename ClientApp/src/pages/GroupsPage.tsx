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
            <CardActionArea component={RouterLink} to={`/groups/${group.id}`} sx={{ p: 2.25, minHeight: 112, '@media (max-width: 350px)': { p: 1.875 } }}>
              <Stack direction="row" alignItems="center" sx={{ gap: 2, '@media (max-width: 350px)': { gap: '11px' } }}>
                <Avatar sx={{ bgcolor: group.type === 'collective' ? 'primary.main' : 'secondary.main', '@media (max-width: 350px)': { width: 36, height: 36 } }}>
                  {group.type === 'collective' ? <Groups2Rounded /> : <PersonRounded />}
                </Avatar>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="h3" sx={{ display: '-webkit-box', overflow: 'hidden', WebkitBoxOrient: 'vertical', WebkitLineClamp: 2 }}>{group.name}</Typography>
                  <Chip size="small" label={`${group.type === 'collective' ? 'Общая' : 'Личная'} · ${group.participantCount} участн.`} sx={{ mt: .75, maxWidth: '100%' }} />
                </Box>
                <Stack spacing={1.5} justifyContent="center" sx={{ alignSelf: 'stretch', minWidth: 114, textAlign: 'right', '@media (max-width: 350px)': { minWidth: 102 } }}>
                  <Box><Typography variant="caption" color="text.secondary" noWrap>Покупки</Typography><Money variant="body2" value={group.totalExpensesKopecks} fontWeight={750} sx={{ whiteSpace: 'nowrap', '@media (max-width: 350px)': { fontSize: 13 } }} /></Box>
                  <Box><Typography variant="caption" color="text.secondary" noWrap>Ваш баланс</Typography><Money variant="body2" value={group.myBalanceKopecks} signed fontWeight={750} sx={{ whiteSpace: 'nowrap', '@media (max-width: 350px)': { fontSize: 13 } }} /></Box>
                </Stack>
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
