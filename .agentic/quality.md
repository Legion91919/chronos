# Quality

## Definition of done
- Acceptance criteria met and demonstrated (one test per criterion, see `docs/DESIGN.md` §13).
- Unit tests for rules (day count, aggregate), property test for projection = replay, store tests for both stores, HTTP acceptance tests, architecture tests.
- CI green: build (warnings as errors), tests + coverage ≥ 70%, format, vulnerable packages, gitleaks, privacy guard, web (vitest + build).
- Docs in `.agentic/` updated in the same change.

## Service levels
| SLI | SLO |
|---|---|
| Command success rate (non-4xx) | 99.9% over 30 days |
| p95 command latency | < 300 ms |
| p95 balance / point-in-time query | < 200 ms (loan streams < 5k events) |
| Projection = replay | 100% (property test; any mismatch is a release blocker) |
| Interest recalculation vs. independent re-performance | exact to the cent |
| Duplicate money movements from retries | 0 |
| Events lost after an acknowledged command | 0 (flushed before 2xx) |
