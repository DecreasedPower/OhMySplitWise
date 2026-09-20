# SplitMoneyTg

Telegram Mini App для учета общих покупок в рублях и сведения долгов между участниками. Старые сценарии чат-бота сохранены как fallback.

## Возможности

- группы с приглашением через Telegram deep link;
- реквизиты пользователя свободным текстом;
- покупки с равным или ручным делением;
- редактирование и удаление покупки автором;
- расчет чистых балансов и рекомендуемых переводов;
- подтверждение перевода отправителем и получателем;
- React-интерфейс на Material 3 с поддержкой светлой и темной темы Telegram;
- PostgreSQL, webhook и защита от повторной обработки updates.

## Запуск

1. Создайте бота через `@BotFather`.
2. Скопируйте `.env.example` в `.env` и заполните значения.
3. Направьте публичный HTTPS URL на порт `8080` приложения.
4. Укажите этот URL в `TELEGRAM_MINI_APP_URL` и настройках Mini App через `@BotFather`.
5. Выполните `docker compose up --build`.
6. Проверьте `GET /health`, затем откройте бота и отправьте `/start`.

`TELEGRAM_WEBHOOK_URL` задается без пути: приложение добавляет `/telegram/webhook`. Если `TELEGRAM_MINI_APP_URL` не задан, кнопка приложения использует webhook URL. Схема базы обновляется миграциями при старте.

## Разработка frontend

Frontend находится в `ClientApp/`. Vite проксирует `/api` на `http://localhost:8080`:

```bash
cd ClientApp
npm ci
npm run dev
```

Для локальной разработки вне Telegram положите валидный `initData` в `sessionStorage` под ключом `tma:initData`. Production API принимает только подписанный Telegram `initData`; пользовательский ID из клиента не считается доверенным.

Проверка frontend:

```bash
cd ClientApp
npm run lint
npm test
npm run build
```

Production Docker image собирает React в отдельном Node stage и отдает готовую статику через ASP.NET. Node.js в итоговый image не входит.

## Production

Конфигурация с PostgreSQL и Caddy находится в `deploy/`. Caddy автоматически получает TLS-сертификат и проксирует запросы в приложение.

На сервере создайте `/opt/ohmysplitwise/.env` с правами `600`:

```env
DOMAIN=bot.example.com
HTTPS_PORT=443
APP_VERSION=production
TELEGRAM_BOT_TOKEN=replace-me
TELEGRAM_WEBHOOK_SECRET=replace-with-a-random-secret
TELEGRAM_MINI_APP_URL=https://bot.example.com
POSTGRES_PASSWORD=replace-with-a-random-password
```

Образ приложения должен быть загружен как `ohmysplitwise:production`. Запуск и обслуживание:

```bash
cd /opt/ohmysplitwise
docker compose up -d
docker compose ps
docker compose logs -f app
docker compose pull postgres caddy
docker compose up -d
```

Для работы сертификата DNS-запись домена должна указывать на сервер, а порты `80` и заданный в `HTTPS_PORT` порт должны быть доступны извне. PostgreSQL не публикуется наружу. Telegram поддерживает webhook-порты `443`, `80`, `88` и `8443`. Для production рекомендуется не менее 1 ГБ RAM и 10 ГБ диска.

### Continuous Deployment

Workflow `.github/workflows/deploy.yml` автоматически собирает и тестирует образ после push в `main`. Развертывание ожидает ручного подтверждения в GitHub Environment `production`. После подтверждения workflow использует заранее установленный `/opt/ohmysplitwise/deploy.sh`, проверяет свободное место с учетом полного несжатого размера нового образа, расширения при импорте и размера базы данных, а затем передает сжатый архив потоком по SSH. VPS распаковывает поток прямо в `docker load`, не создавая архив на диске. Активный образ сохраняется для отката. Если запуск приложения или проверка `/health` завершается ошибкой, скрипт возвращает предыдущий образ и отдельно проверяет его `/health`. Дамп базы данных создается перед развертыванием, но автоматически при откате приложения не восстанавливается. При изменении `deploy/deploy.sh` установите его на VPS отдельно под root до запуска workflow.

GitHub Environment должен содержать секреты `VPS_HOST`, `VPS_HOST_KEY` и `VPS_SSH_PRIVATE_KEY`. Секреты приложения остаются только в `/opt/ohmysplitwise/.env` на сервере.

### Очистка резервных копий

Скрипт `deploy/cleanup-backups.sh` сохраняет три последних дампа в `/opt/ohmysplitwise/backups` и удаляет более старые. Для ежедневного запуска установите скрипт и systemd units на VPS:

```bash
curl -fsSL https://raw.githubusercontent.com/DecreasedPower/OhMySplitWise/<COMMIT_SHA>/deploy/install-backup-cleanup.sh | sudo bash -s -- <COMMIT_SHA>
sudo /opt/ohmysplitwise/cleanup-backups.sh --dry-run
sudo /opt/ohmysplitwise/cleanup-backups.sh
sudo systemctl enable --now ohmysplitwise-backup-cleanup.timer
systemctl status ohmysplitwise-backup-cleanup.timer
systemctl list-timers ohmysplitwise-backup-cleanup.timer
```
