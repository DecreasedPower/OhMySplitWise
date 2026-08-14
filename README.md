# SplitMoneyTg

Telegram-бот для учета общих покупок в рублях и сведения долгов между участниками.

## Возможности

- группы с приглашением через Telegram deep link;
- реквизиты пользователя свободным текстом;
- покупки с равным или ручным делением;
- редактирование и удаление покупки автором;
- расчет чистых балансов и рекомендуемых переводов;
- подтверждение перевода отправителем и получателем;
- PostgreSQL, webhook и защита от повторной обработки updates.

## Запуск

1. Создайте бота через `@BotFather`.
2. Скопируйте `.env.example` в `.env` и заполните значения.
3. Направьте публичный HTTPS URL на порт `8080` приложения.
4. Выполните `docker compose up --build`.
5. Проверьте `GET /health`, затем откройте бота и отправьте `/start`.

`TELEGRAM_WEBHOOK_URL` задается без пути: приложение добавляет `/telegram/webhook`. Схема базы обновляется миграциями при старте.

## Production

Конфигурация с PostgreSQL и Caddy находится в `deploy/`. Caddy автоматически получает TLS-сертификат и проксирует запросы в приложение.

На сервере создайте `/opt/ohmysplitwise/.env` с правами `600`:

```env
DOMAIN=bot.example.com
APP_VERSION=production
TELEGRAM_BOT_TOKEN=replace-me
TELEGRAM_WEBHOOK_SECRET=replace-with-a-random-secret
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

Для работы сертификата DNS-запись домена должна указывать на сервер, а порты `80` и `443` должны быть доступны извне. PostgreSQL не публикуется наружу. Для production рекомендуется не менее 1 ГБ RAM и 10 ГБ диска.

### Continuous Deployment

Workflow `.github/workflows/deploy.yml` автоматически собирает и тестирует образ после push в `main`. Развертывание ожидает ручного подтверждения в GitHub Environment `production`. После подтверждения образ передается на VPS по SSH; `deploy/deploy.sh` обновляет приложение, проверяет `/health` и автоматически откатывает предыдущую версию при ошибке.

GitHub Environment должен содержать секреты `VPS_HOST`, `VPS_HOST_KEY` и `VPS_SSH_PRIVATE_KEY`. Секреты приложения остаются только в `/opt/ohmysplitwise/.env` на сервере.

### Очистка резервных копий

Скрипт `deploy/cleanup-backups.sh` сохраняет три последних дампа в `/opt/ohmysplitwise/backups` и удаляет более старые. Для ежедневного запуска установите скрипт и systemd units на VPS:

```bash
sudo install -o root -g root -m 755 deploy/cleanup-backups.sh /opt/ohmysplitwise/cleanup-backups.sh
sudo install -o root -g root -m 644 deploy/ohmysplitwise-backup-cleanup.service /etc/systemd/system/ohmysplitwise-backup-cleanup.service
sudo install -o root -g root -m 644 deploy/ohmysplitwise-backup-cleanup.timer /etc/systemd/system/ohmysplitwise-backup-cleanup.timer
sudo systemctl daemon-reload
sudo /opt/ohmysplitwise/cleanup-backups.sh --dry-run
sudo /opt/ohmysplitwise/cleanup-backups.sh
sudo systemctl enable --now ohmysplitwise-backup-cleanup.timer
systemctl status ohmysplitwise-backup-cleanup.timer
systemctl list-timers ohmysplitwise-backup-cleanup.timer
```
