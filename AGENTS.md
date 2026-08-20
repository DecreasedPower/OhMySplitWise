# Repository Guide

## Toolchain and Verification

- The solution targets .NET 10; CI installs `10.0.x` and verifies the test project, not the solution file.
- Match CI with this order:
  ```bash
  dotnet restore SplitMoneyTg.Tests/SplitMoneyTg.Tests.csproj
  dotnet build SplitMoneyTg.Tests/SplitMoneyTg.Tests.csproj --configuration Release --no-restore
  dotnet test SplitMoneyTg.Tests/SplitMoneyTg.Tests.csproj --configuration Release --no-build --logger "console;verbosity=normal"
  ```
- Run one test with `dotnet test SplitMoneyTg.Tests/SplitMoneyTg.Tests.csproj --filter "FullyQualifiedName~SplitMoneyTg.Tests.BalanceServiceTests.SplitEqually_PreservesEveryKopeck"`.
- The unit tests use EF Core InMemory. They do not cover PostgreSQL migrations, raw SQL, row locks, or the webhook endpoint; use `docker compose up --build` for those paths. Compose reads `.env` and exposes the app on port `8080`.

## Runtime Wiring

- `Program.cs` is the web entrypoint. It maps only `POST /telegram/webhook` and `GET /health`, runs EF migrations plus a participant backfill at every startup, and then registers the Telegram webhook when `Telegram__WebhookUrl` is non-empty.
- A direct `dotnet run` does not load the repository `.env`. Runtime startup requires `ConnectionStrings__Postgres`, `Telegram__BotToken`, and `Telegram__WebhookSecret`; `Telegram__WebhookUrl` is optional. Its value is the public base URL because the app appends `/telegram/webhook`.
- `Telegram/BotHandler.cs` contains the bot's routing and multi-step flows. Flow state is persisted in `UserSession.DataJson`; callback payload formats and state names must stay synchronized with their handlers.
- Telegram updates are deduplicated through `ProcessedUpdates`. Preserve that behavior when changing request handling.

## Domain and Persistence

- Store money as `long` kopecks. Parsing/formatting and equal/manual splitting live in `BalanceService` and `BotHandler`; do not introduce floating-point amounts.
- `GroupMember` controls Telegram-user access to a group. `GroupParticipant` is the identity used by expenses, shares, balances, and transfers. Standalone groups can contain managed participants with negative IDs and no Telegram user; never use a participant's list index or assume IDs are positive Telegram IDs.
- Managed-participant add/rename/delete operations and expense saves in `BotHandler` use PostgreSQL `FOR UPDATE` locking and revalidation; expense changes also cancel stale pending transfers. Preserve that pattern for changes that depend on current participants or balances.
- Schema changes require an EF migration and an updated `Migrations/AppDbContextModelSnapshot.cs`; startup applies migrations automatically. The standalone-group migration also installs a PostgreSQL trigger, so model behavior is not fully represented by EF InMemory tests.

## Delivery

- Pushes to `main` run both CI and production-image deployment. Deployment builds `linux/amd64`, waits at the GitHub `production` environment, then `/opt/ohmysplitwise/deploy.sh` backs up PostgreSQL and rolls back if the production health check fails.
- Local compose (`compose.yaml`, service `splitmoneytg`) and production compose (`deploy/compose.yaml`, service `app`) intentionally use different image and service names.
