# Runbook

## Prerequisites
.NET 8 SDK, Node 22. Packages: `nuget.config` restores `Agentic.Blocks.*` from the vendored `eng/packages/` and everything else from nuget.org (ADR-0003).

## Run locally
```bash
# API on :5204 (Development seeds three invented loans; events in src/Chronos.Api/App_Data/events.jsonl)
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5204 dotnet run --project src/Chronos.Api
# Web on :5174 (Vite proxies /api and /health to :5204)
cd web && npm ci && npm run dev
```
Config: `EventStore:Kind` (`File` | `InMemory`), `EventStore:Path`, `Ledger:MaxAttempts` (default 10), `Seed:Enabled`.
Reset demo data: stop the API and delete `src/Chronos.Api/App_Data/events.jsonl` (demo only; production events are never deleted).

## Run tests
```bash
dotnet test Chronos.sln                     # CI / any machine with nuget.org
cd web && npx vitest run && npm run build
```
Offline sandbox (no nuget.org): `/home/claude/tools/run-xunit-offline.sh tests/Chronos.Tests src/Chronos.Api/Chronos.Api.csproj tests/offline/OfflineTestRefs.csproj`.

## Deploy
Single instance (the file store and in-memory idempotency/outbox stores are single-process). Before a second instance: database `IEventStore` with unique `(stream_id, version)`, shared idempotency and outbox stores (slice 2).

## Monitor and alerts
- `/health/live`, `/health/ready` (ready reads the event store).
- `chronos.ledger.concurrency_conflicts` rising: hot loan or a stuck client; `agentic.blocks.outbox.dead_lettered` > 0: the cash desk feed is failing.

## Common incidents
- **Projection looks wrong**: `POST /api/projections/rebuild` (Development) or restart; projections are disposable, events are not.
- **API won't start, "gap at position n"**: the event file was edited by hand. Restore from backup; never edit events.
- **409 `ledger.concurrency_conflict`**: retry with the same Idempotency-Key; it is safe.
- **400 `ledger.period_closed` / `ledger.backdated`**: a correction for a closed period is a new compensating event, not an edit.
