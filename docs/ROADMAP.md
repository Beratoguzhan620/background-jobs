# Roadmap

The background work of a small online shop's back office: importing large order files, sales reports on a
schedule and housekeeping, run by Hangfire on PostgreSQL. The point is dependable jobs: they survive
restarts, retry with back-off, never run twice at the same time, can be cancelled, and every step is
visible to the people operating them.

## Scope

- Admin and viewer accounts. The first admin comes from configuration; admins add the others.
- **Imports:** upload a CSV of orders (up to 50 MB). It is streamed to disk, processed in chunks with
  progress, rows that fail validation go to a downloadable error file, and the import can be cancelled.
  The same file twice is refused.
- **Reports:** a sales report for a date range, now or at a chosen time; a nightly report for the
  previous day. Downloadable as CSV.
- **Housekeeping:** an hourly job that deletes stored uploads past their retention period.
- **Failures:** automatic retries with growing delays, a list of failed runs and a manual retry.
- **No double runs:** a duplicate request is refused, and a job picked up by two workers runs once.
- **Monitoring:** job status API and statistics, health checks, and the Hangfire dashboard for admins.
- **Audit trail:** who started, cancelled or retried what, including actions taken in the dashboard.

Out of scope: emailing reports (that is the notification-service project), multi-tenant data.

## Data model

| Table | Notes |
|---|---|
| `users` | email (unique), display name, role (`Admin`, `Viewer`), password hash |
| `job_runs` | kind (`Import`, `Report`, `Cleanup`), status (`Queued`, `Running`, `Retrying`, `Succeeded`, `Failed`, `Cancelled`), dedup key, Hangfire job id, requested by, progress, attempts, heartbeat, timestamps, error, result summary |
| `import_files` | job run, original name, stored path, size, SHA-256, rows read / imported / duplicate / rejected, error file |
| `orders` | order number (unique), date, customer email, SKU, quantity, unit price in minor units, source import |
| `reports` | job run, period, stored CSV, summary |
| `audit_log` | actor, action, target, details, time |

Hangfire keeps its own tables in the `hangfire` schema.

## Architecture

```
src/BackgroundJobs.Core            Entities, job logic (import, report, cleanup), CSV parsing, JobRunner
src/BackgroundJobs.Infrastructure  EF Core + Npgsql, migrations, file storage, Hangfire configuration
src/BackgroundJobs.Api             REST API (JWT), dashboard (cookie sign-in, Admin role), enqueues jobs
src/BackgroundJobs.Worker          Hangfire server; runs the jobs. Compose starts two of them.
tests/BackgroundJobs.UnitTests
tests/BackgroundJobs.IntegrationTests  Real PostgreSQL in Testcontainers and in-process Hangfire servers
```

## Key decisions

- **API and workers are separate processes.** The API only writes jobs to storage; workers can be scaled
  or restarted without touching the API.
- **Our own `job_runs` table next to Hangfire's.** Hangfire knows queues and retries; the business needs
  progress, results, who asked for it and a stable id. The two are linked by the Hangfire job id.
- **A run is claimed before it does any work.** One conditional `UPDATE` moves it to `Running`. Hangfire
  delivers at least once, so a second worker (or a retry racing a slow first attempt) finds nothing to
  claim and stops. A heartbeat lets a run abandoned by a crashed worker be claimed again.
- **Duplicates are refused by the database.** A partial unique index on the dedup key covers active and
  successful runs, so two identical requests at the same moment cannot both get in. Failed runs can be
  tried again.
- **Imports are idempotent row by row.** Orders are inserted with `ON CONFLICT (order_no) DO NOTHING`, so a
  retried import never creates duplicates.
- **Cancellation is cooperative.** Cancelling marks the run; the job notices at its next progress update
  and stops cleanly instead of being killed mid-write.
- **People sign in as themselves.** The API uses JWT and the dashboard a cookie, both from the same
  accounts and roles, so every action in the audit trail has a name.

## Stages

| # | Branch | Content |
|---|---|---|
| 01 | `stage/01-skeleton` | Projects, Hangfire on PostgreSQL, Compose with two workers, CI, users and roles, JWT sign-in, audit trail, job run API |
| 02 | `stage/02-imports` | JobRunner (claim, progress, heartbeat, cancellation), streamed upload, chunked import, error file |
| 03 | `stage/03-scheduling` | Reports now or later, nightly report, cleanup, retries with back-off, manual retry, cancel, duplicate refusal |
| 04 | `stage/04-monitoring` | Dashboard behind cookie sign-in and the Admin role, dashboard actions audited, statistics, health checks, two-worker tests, end-to-end flow |
| 05 | `stage/05-docs` | README (EN + TR), architecture, design decisions, screenshots and walkthrough |

## Later

- Send finished reports through the notification-service.
- Purge Mini Banking API idempotency keys as a recurring job against its database.
- Per-tenant queues and rate limits.
