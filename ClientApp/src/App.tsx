import LaunchRounded from '@mui/icons-material/LaunchRounded'
import Telegram from '@mui/icons-material/Telegram'
import { Box, Button, Card, Typography } from '@mui/material'
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useSessionExpired } from './api/session'
import { useClientUpgradeRequired } from './api/upgrade'
import { PageLoader } from './components/AsyncState'
import { GroupsPage } from './pages/GroupsPage'
import { closeTelegram, isTelegram, useTelegramLifecycle } from './platform/telegram'

const CreateGroupPage = lazy(() => import('./pages/CreateGroupPage').then((module) => ({ default: module.CreateGroupPage })))
const GroupPage = lazy(() => import('./pages/GroupPage').then((module) => ({ default: module.GroupPage })))
const ExpensesPage = lazy(() => import('./pages/ExpensesPage').then((module) => ({ default: module.ExpensesPage })))
const ExpenseFormPage = lazy(() => import('./pages/ExpenseFormPage').then((module) => ({ default: module.ExpenseFormPage })))
const ExpenseDetailPage = lazy(() => import('./pages/ExpenseDetailPage').then((module) => ({ default: module.ExpenseDetailPage })))
const ParticipantsPage = lazy(() => import('./pages/ParticipantsPage').then((module) => ({ default: module.ParticipantsPage })))
const BalancesPage = lazy(() => import('./pages/BalancesPage').then((module) => ({ default: module.BalancesPage })))
const InvitePage = lazy(() => import('./pages/InvitePage').then((module) => ({ default: module.InvitePage })))
const ProfilePage = lazy(() => import('./pages/ProfilePage').then((module) => ({ default: module.ProfilePage })))

export function App() {
  useTelegramLifecycle()
  const sessionExpired = useSessionExpired()
  const upgradeRequired = useClientUpgradeRequired()
  if (!isTelegram() && import.meta.env.PROD) return <OutsideTelegram />
  if (upgradeRequired) return <UpgradeRequired />
  if (sessionExpired) return <ExpiredSession />
  return <Suspense fallback={<PageLoader />}><Routes>
    <Route path="/" element={<GroupsPage />} />
    <Route path="/groups/new" element={<CreateGroupPage />} />
    <Route path="/groups/:groupId" element={<GroupPage />} />
    <Route path="/groups/:groupId/expenses" element={<ExpensesPage />} />
    <Route path="/groups/:groupId/expenses/new" element={<ExpenseFormPage />} />
    <Route path="/groups/:groupId/expenses/:expenseId" element={<ExpenseDetailPage />} />
    <Route path="/groups/:groupId/expenses/:expenseId/edit" element={<ExpenseFormPage />} />
    <Route path="/groups/:groupId/participants" element={<ParticipantsPage />} />
    <Route path="/groups/:groupId/balances" element={<BalancesPage />} />
    <Route path="/groups/:groupId/invite" element={<InvitePage />} />
    <Route path="/profile" element={<ProfilePage />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></Suspense>
}

export function UpgradeRequired() {
  return <Box sx={{ minHeight: '100dvh', display: 'grid', placeItems: 'center', p: 2 }}>
    <Card sx={{ p: 4, maxWidth: 440, textAlign: 'center' }}>
      <Typography component="h1" variant="h1">Нужно обновить приложение</Typography>
      <Typography color="text.secondary" sx={{ mt: 2 }}>Закройте мини-приложение и откройте его снова в Telegram, чтобы продолжить.</Typography>
      <Button variant="contained" onClick={closeTelegram} sx={{ mt: 3 }}>Закрыть приложение</Button>
    </Card>
  </Box>
}

function ExpiredSession() {
  return <Box sx={{ minHeight: '100dvh', display: 'grid', placeItems: 'center', p: 2 }}>
    <Card sx={{ p: 4, maxWidth: 440, textAlign: 'center' }}>
      <Typography component="h1" variant="h1">Сессия истекла</Typography>
      <Typography color="text.secondary" sx={{ mt: 2 }}>Закройте мини-приложение, затем откройте его снова из Telegram.</Typography>
      <Button variant="contained" onClick={closeTelegram} sx={{ mt: 3 }}>Закрыть приложение</Button>
    </Card>
  </Box>
}

function OutsideTelegram() {
  const botUrl = import.meta.env.VITE_TELEGRAM_BOT_URL as string | undefined
  return <Box sx={{ minHeight: '100dvh', display: 'grid', placeItems: 'center', p: 2, backgroundImage: 'radial-gradient(circle at 50% 0%, rgba(23,107,82,.18), transparent 45%)' }}>
    <Card sx={{ p: 4, maxWidth: 440, textAlign: 'center' }}>
      <Telegram color="primary" sx={{ fontSize: 64 }} />
      <Typography component="h1" variant="h1" sx={{ mt: 2 }}>Откройте приложение в Telegram</Typography>
      <Typography color="text.secondary" sx={{ mt: 2 }}>Так мы безопасно определим ваш профиль и покажем ваши группы.</Typography>
      {botUrl && <Button component="a" href={botUrl} variant="contained" endIcon={<LaunchRounded />} sx={{ mt: 3 }}>Перейти в Telegram</Button>}
    </Card>
  </Box>
}
