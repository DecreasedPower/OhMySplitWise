# SplitMoney Telegram Mini App

Mobile-first React client for the SplitMoney service. It is intentionally isolated from the backend project.

## Commands

```bash
npm install
npm run dev
npm test
npm run build
```

Vite proxies `/api` to `http://localhost:8080` in development. To exercise the app outside Telegram during development, set a Telegram `initData` string in session storage under `tma:initData`. Production deliberately shows an "open in Telegram" screen when signed init data is absent. Set `VITE_TELEGRAM_BOT_URL` at build time to show a direct bot link on that screen.

## Assumed API contract

The current backend has no HTTP API beyond the Telegram webhook and health check. This client assumes the following same-origin REST endpoints. Requests use `Authorization: tma <Telegram.WebApp.initData>`, `Accept: application/json`, and JSON request bodies. Errors use RFC 7807 JSON (`title`, `detail`, and optional `code`). A successful delete may return `204`.

- `GET /api/me`, `PATCH /api/me`
- `GET/POST /api/groups`, `GET/DELETE /api/groups/{groupId}`
- `DELETE /api/groups/{groupId}/membership`
- `GET/POST /api/groups/{groupId}/participants`
- `PATCH/DELETE /api/groups/{groupId}/participants/{participantId}`
- `GET/POST /api/groups/{groupId}/expenses`
- `GET/PUT/DELETE /api/groups/{groupId}/expenses/{expenseId}`
- `GET /api/groups/{groupId}/balances`
- `POST /api/groups/{groupId}/transfers`
- `PATCH /api/groups/{groupId}/transfers/{transferId}`
- `POST /api/groups/{groupId}/invitations`

JSON names are camelCase. Every .NET `long`/`Int64`, including Telegram IDs, participant IDs, and all `*Kopecks` values, is encoded as a decimal JSON string. UUID group, expense, invitation, and transfer IDs are also strings. Dates are ISO 8601 strings. `Group.type` is `collective` or `standalone`; `Group.isOwner` is the authenticated user's authorization flag. Lists return plain arrays rather than pagination envelopes. Expense detail includes `shares`; list items may omit them.

The balance response shape is:

```json
{
  "balances": [{ "participantId": "123", "participantName": "Анна", "amountKopecks": "25050" }],
  "suggestions": [{
    "fromParticipantId": "456", "fromName": "Борис", "toParticipantId": "123", "toName": "Анна",
    "amountKopecks": "25050", "paymentDetails": "...", "canMarkPaid": true, "pendingTransferId": null
  }],
  "pendingTransfers": [{ "id": "uuid", "fromName": "Борис", "toName": "Анна", "amountKopecks": "25050", "canResolve": true }]
}
```

Creating/updating an expense sends exact shares as `{ description, amountKopecks, payerId, shares: [{ participantId, amountKopecks }] }`. The server remains responsible for authorization, active-participant revalidation, row locking, exact share-total validation, and cancellation of stale pending transfers.
