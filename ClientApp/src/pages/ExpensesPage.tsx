import AddRounded from '@mui/icons-material/AddRounded'
import ReceiptLongRounded from '@mui/icons-material/ReceiptLongRounded'
import { Avatar, Box, Card, CardActionArea, Fab, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useParams } from 'react-router-dom'
import { endpoints } from '../api/endpoints'
import { ErrorState, PageLoader } from '../components/AsyncState'
import { Money } from '../components/Money'
import { Page } from '../components/Page'

export function ExpensesPage() {
  const { groupId = '' } = useParams()
  const query = useQuery({ queryKey: ['expenses', groupId], queryFn: () => endpoints.expenses(groupId) })
  if (query.isPending) return <PageLoader />
  if (query.isError) return <Page title="Покупки"><ErrorState error={query.error} retry={() => query.refetch()} /></Page>
  return <Page title="Покупки" eyebrow={`${query.data.length} записей`}>
    <Stack spacing={1.25}>
      {query.data.length === 0 && <Box sx={{ py: 9, textAlign: 'center' }}><ReceiptLongRounded color="disabled" sx={{ fontSize: 56 }} /><Typography variant="h3" sx={{ mt: 2 }}>Покупок пока нет</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>Добавьте первый общий расход.</Typography></Box>}
      {query.data.map((expense) => <Card key={expense.id}><CardActionArea component={RouterLink} to={`/groups/${groupId}/expenses/${expense.id}`} sx={{ p: 2 }}>
        <Stack direction="row" alignItems="center" spacing={1.5}>
          <Avatar sx={{ bgcolor: 'background.default', color: 'primary.main' }}><ReceiptLongRounded /></Avatar>
          <Box sx={{ flex: 1, minWidth: 0 }}><Typography fontWeight={750} noWrap>{expense.description}</Typography><Typography variant="body2" color="text.secondary">{expense.payerName} · {new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'short' }).format(new Date(expense.createdAt))}</Typography></Box>
          <Money value={expense.amountKopecks} fontWeight={750} />
        </Stack>
      </CardActionArea></Card>)}
    </Stack>
    <Fab component={RouterLink} to={`/groups/${groupId}/expenses/new`} color="primary" aria-label="Добавить покупку" sx={{ position: 'fixed', right: 20, bottom: 'calc(20px + env(safe-area-inset-bottom))' }}><AddRounded /></Fab>
  </Page>
}
