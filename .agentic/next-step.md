# Next step

- **Doing:** slice 1 complete (event-sourced ledger + loan-position screen); not yet committed.
- **Done so far:** ADR-0002/0003, sketch leg, API on :5204, 49 .NET tests + 9 vitest tests green, docs/DESIGN.md.
- **Next action:** first push to get CI green (`dotnet restore Chronos.sln` uses `eng/packages`), then slice 2 auth: `AddBlocksAuth` with roles ops/finance/auditor, replacing the `X-Actor` header in `src/Chronos.Api/LoanEndpoints.cs`.
- **Blocked by:** nothing
