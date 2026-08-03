# Database operations and migration guide

This runbook is for operators of the Bug Tracker database. Run commands from the repository root unless stated otherwise. Examples use `bug_tracker.db`, the path currently configured by `src/BugTracker.Api/appsettings*.json`. Replace it when `Database:Path` is overridden.

The application uses SQLite in WAL mode, applies ordered embedded migrations at startup, and records their SHA-256 checksums in `schema_migrations`. Every application connection enables and verifies SQLite foreign-key enforcement.

## Operator rules

- Stop all writers for restores, file replacement, destructive maintenance, and schema upgrades unless a procedure explicitly says an online operation is safe.
- Never copy only `bug_tracker.db` while the API is running. In WAL mode, committed data may still be in `bug_tracker.db-wal`; the `-wal` and `-shm` files are a related set.
- Never edit an already-applied migration. Add a new migration and preserve the old checksum.
- Take and verify a backup before every upgrade. Keep the source backup until the upgrade and business validation are complete.
- Test every upgrade and restore against a disposable copy first. Do not use backend tests against the development database; the test project already uses isolated temporary databases.
- Use a local filesystem with reliable locking. Do not place the live SQLite files on NFS/SMB, in a synchronized folder, or in an application image.

## 1. Create a new empty SQLite database

Set `Database:Path` in configuration or `Database__Path` in the environment and start the API. The migration runner creates the parent directory, database, complete schema, and migration journal. A new installation contains no users, projects, or tickets.

```bash
umask 077
DB="/absolute/path/to/bug_tracker.db"
test ! -e "$DB" || { printf '%s\n' "Refusing to overwrite $DB" >&2; exit 1; }
Database__Path="$DB" dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- migrate
sqlite3 "$DB" "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA quick_check; PRAGMA foreign_key_check;"
```

The command exits after migration. Expected validation output includes `wal` and `ok`; `foreign_key_check` must return no rows. Confirm the migration and empty business tables:

```bash
sqlite3 "$DB" "SELECT type, name FROM sqlite_master WHERE type IN ('table','index','trigger') ORDER BY type,name;"
sqlite3 "$DB" "SELECT version,name,checksum,applied_at FROM schema_migrations ORDER BY version;"
sqlite3 "$DB" "SELECT (SELECT COUNT(*) FROM users) AS users, (SELECT COUNT(*) FROM projects) AS projects, (SELECT COUNT(*) FROM bug_tickets) AS tickets;"
```

Create the first administrator with a local one-time command. The password is read without terminal echo, or from the environment for noninteractive provisioning:

```bash
Database__Path="$DB" dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- bootstrap-admin --email owner@example.com

# Noninteractive secret injection; do not store this value in shell history or source control.
BUG_TRACKER_BOOTSTRAP_PASSWORD='<secret>' Database__Path="$DB" \
  dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- bootstrap-admin --email owner@example.com
```

The command refuses to run after an administrator exists. The API opens the file with `ReadWriteCreate`, sets WAL, `synchronous=NORMAL`, and a 10-second write busy timeout. Ensure the service account owns the database directory because SQLite must create `-wal` and `-shm` siblings.

## 2. Schema migrations and checksums

Migration files are immutable embedded resources under `src/BugTracker.Api/Database/Migrations/`. `SqliteMigrationRunner` validates every recorded checksum, rejects unknown versions, and applies each pending migration and its journal row in one transaction. A nonempty unversioned database is rejected instead of being modified heuristically.

Record the applied versions and exact schema used for an environment:

```bash
sqlite3 "$DB" "SELECT version,name,checksum,applied_at FROM schema_migrations ORDER BY version;"
sqlite3 "$DB" ".schema" > /tmp/bug-tracker-schema.actual.sql
sha256sum /tmp/bug-tracker-schema.actual.sql
```

The `.schema` checksum is useful for comparing identically generated databases, but it is not a migration version because formatting and object order can differ. Keep migration rows and the deployment artifact identity with the backup manifest. Never edit an applied migration; add the next numbered SQL resource and register it in `SqliteMigrationRunner`.

SQLite table rebuilds must use an explicit create/copy/validate/swap sequence. Preserve indexes, triggers, constraints, defaults, and foreign keys; compare source and target counts before dropping the old table. A migration that cannot be fully transactional must document its recovery point and be rehearsed from backup. Do not introduce an ad-hoc shell loop that marks a migration successful independently of its DDL.

## 3. Backup and restore

### Online, WAL-safe logical snapshot

SQLite's backup API is the preferred online method. The CLI `.backup` command uses it and produces a consistent single-file snapshot while the API remains online:

```bash
mkdir -p backups
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
sqlite3 "$DB" ".timeout 10000" ".backup 'backups/bug_tracker-${STAMP}.db'"
sqlite3 "backups/bug_tracker-${STAMP}.db" "PRAGMA quick_check; PRAGMA foreign_key_check;"
sha256sum "backups/bug_tracker-${STAMP}.db" > "backups/bug_tracker-${STAMP}.db.sha256"
```

The backup is not successful unless `quick_check` returns `ok`, `foreign_key_check` returns no rows, and the checksum manifest is retained. Copy the verified snapshot and manifest to encrypted storage outside the host. Periodically restore one to a disposable location and exercise API smoke tests.

`VACUUM INTO` can also create a compact, transactionally consistent snapshot, but the destination must not already exist and it requires additional temporary disk space:

```bash
sqlite3 "$DB" "VACUUM INTO 'backups/bug_tracker-compact-${STAMP}.db';"
```

### Offline physical copy

For a byte-level copy, stop the API and every other database client first. Then checkpoint and copy only after confirming the command succeeds:

```bash
sqlite3 "$DB" "PRAGMA wal_checkpoint(TRUNCATE);"
```

After the checkpoint, copy the database file with the host's approved backup tool. If a clean shutdown/checkpoint cannot be guaranteed, copy `DB`, `DB-wal`, and `DB-shm` together while all processes remain stopped; never mix files from different times. Do not delete WAL/SHM files to “fix” a database.

### Restore

1. Stop the API and verify no process has the database open.
2. Preserve the failed/current database and any `-wal`/`-shm` siblings under a timestamped quarantine name; do not overwrite the only forensic copy.
3. Verify the backup checksum, copy it to a new staging path, and run the full validation in section 5.
4. Replace the configured database with the validated file. Remove stale destination `-wal`/`-shm` files only because all clients are stopped and the restored snapshot is a standalone backup-API output.
5. Ensure ownership and permissions are correct, start one API instance, run smoke tests, then admit traffic.

Restore is complete only after authentication, project visibility, active/closed ticket reads, attachment download, and one reversible test write have been checked. Do not restore a production database into a less trusted environment without redaction and secret/token handling approval.

## 4. Demo database rebuild and seed

`seed-demo` is a backend-owned transactional seed. It generates supported password hashes and inserts deterministic synthetic users, projects, allocations, tickets, and activity in foreign-key order. It refuses a database containing any users, projects, or tickets and must never be run against production.

```bash
DEMO_DB="/tmp/bug_tracker-demo-$(date -u +%Y%m%dT%H%M%SZ).db"
Database__Path="$DEMO_DB" dotnet run --project src/BugTracker.Api/BugTracker.Api.csproj -- seed-demo
sqlite3 "$DEMO_DB" "PRAGMA quick_check; PRAGMA foreign_key_check;"
```

Credentials are documented in `demo.md`. To replace the repository demo, build and validate a newly named file first, stop the API, retain a backup of the previous file, and then atomically switch the configured path. Do not reset a live file in place.

## 5. Integrity, foreign-key, and content validation

Run validation against a backup snapshot when possible. For maintenance against the live file, avoid a long `integrity_check` during peak load.

```bash
sqlite3 "$DB" "PRAGMA quick_check;"
sqlite3 "$DB" "PRAGMA integrity_check;"
sqlite3 "$DB" "PRAGMA foreign_keys=ON; PRAGMA foreign_key_check;"
```

Both integrity commands must return exactly `ok`; the FK command must return no rows. `PRAGMA foreign_keys` is connection-local, so setting it in one CLI session does not prove that application connections enforce it.

Validate JSON-bearing columns and core invariants:

```bash
sqlite3 "$DB" "SELECT 'users.projects_json',user_id FROM users WHERE NOT json_valid(projects_json); SELECT 'bug_tickets.tags_json',id FROM bug_tickets WHERE NOT json_valid(tags_json); SELECT 'bug_tickets.report_images_json',id FROM bug_tickets WHERE report_images_json IS NOT NULL AND NOT json_valid(report_images_json); SELECT 'bug_tickets.resolution_report_images_json',id FROM bug_tickets WHERE resolution_report_images_json IS NOT NULL AND NOT json_valid(resolution_report_images_json); SELECT 'bug_tickets.text_evidence_json',id FROM bug_tickets WHERE text_evidence_json IS NOT NULL AND NOT json_valid(text_evidence_json);"
sqlite3 "$DB" "SELECT status,COUNT(*) FROM bug_tickets GROUP BY status ORDER BY status; SELECT COUNT(*) AS attachment_rows,COALESCE(SUM(size_bytes),0) AS attachment_bytes FROM ticket_attachments;"
```

Before and after migration, capture per-table row counts, ID uniqueness, null counts for required fields, status/role/type distributions, minimum/maximum timestamps, attachment byte totals, and the set of indexes/triggers. Compare business relationships (project memberships, reporter/assignee/resolver users, ticket activity, notifications, and attachments), not just total rows.

## 6. Failure recovery

| Symptom | Safe response |
|---|---|
| Migration or startup fails | Stop the API; capture logs and all DB/WAL/SHM files; do not rerun repeatedly. Validate a copy. If the state is uncertain, restore the pre-upgrade backup. |
| `database is locked` / `SQLITE_BUSY` | Identify long readers and competing writers; allow the configured timeout/retry path to finish. Do not kill the process or remove WAL files. Quiesce traffic and checkpoint only after clients release transactions. Repeated contention is a PostgreSQL trigger. |
| WAL grows continuously | Find long-lived readers preventing checkpoints, verify free disk, then quiesce them and run `PRAGMA wal_checkpoint(TRUNCATE)`. Never truncate the WAL at filesystem level. |
| Disk full / I/O error | Stop writes, preserve files, restore free space without moving an open DB, and validate a copy. Resume only after filesystem health and headroom are confirmed. |
| `quick_check`, `integrity_check`, or FK check fails | Remove the instance from service and preserve forensic copies. Prefer restore from the newest validated backup. Use SQLite `.recover` only as a last-resort salvage into a new file, then perform complete row-level validation; never overwrite the damaged source. |
| Bad data but structurally valid DB | Stop affected writes, determine the exact time/range, and use an audited corrective migration or restore. Do not hand-edit production rows without a reviewed script, backup, and reconciliation query. |

Define recovery objectives before production use. Backup frequency must make the recovery point objective achievable, and restore drills must demonstrate the recovery time objective; untested backups are not a recovery plan.

## 7. Safe SQLite upgrade checklist

### Before

- [ ] Change, owner, maintenance window, expected schema version/checksum, and rollback decision time are recorded.
- [ ] Only a supported source schema is being upgraded; schema drift has been compared.
- [ ] Free disk accommodates the database, WAL, backup, and any table rebuild (use at least one extra full database copy; more for `VACUUM`).
- [ ] A WAL-safe backup has passed checksum, `quick_check`, `integrity_check`, and FK validation, and a restore drill is known-good.
- [ ] Migration was rehearsed on a recent production-sized snapshot with duration and lock impact recorded.
- [ ] Writers are drained and only one API instance will run migrations.

### During

- [ ] Start one application instance or use the approved migration runner; do not run concurrent migrators.
- [ ] Capture logs, start/end time, database/WAL size, and each applied checksum.
- [ ] On unexpected error, stop and assess; do not blindly retry.

### After

- [ ] `quick_check`, `integrity_check`, `foreign_key_check`, JSON checks, migration-specific assertions, row counts, indexes, and triggers pass.
- [ ] API smoke tests cover auth, projects, active/closed tickets, comments/activity, upload/download, close/reopen, notifications, and authorization boundaries.
- [ ] Observe error rate, busy/locked events, write latency, WAL growth, disk, and attachment growth before restoring normal traffic.
- [ ] Keep the pre-upgrade backup through the rollback window and document the result.

## 8. SQLite operating envelope and PostgreSQL triggers

SQLite is appropriate for a single-host, modest-write deployment. Its defining operational limit is one writer at a time, even in WAL mode. WAL improves reader/writer concurrency but does not create parallel writers or multi-host high availability. The current app uses 5-second read and 10-second write busy timeouts; retries hide brief contention but do not increase write capacity.

Operationally:

- keep the database on local durable storage and run one API writer deployment;
- monitor DB/WAL/SHM size, disk latency and free space, backup duration, restore duration, checkpoint progress, busy/locked rate, transaction duration, and p95/p99 API write latency;
- avoid long transactions and unbounded reports on the writer;
- schedule `PRAGMA optimize;` after substantial index/data changes; use `VACUUM` only in a planned window with sufficient free space;
- note that attachment uploads currently allow up to 3 images per ticket and 25,000,000 bytes each. Storing those BLOBs in SQLite can add roughly 75 MB per ticket, enlarge backups/WAL, and dominate recovery time;
- treat SQLite's very high compiled theoretical size/row limits as irrelevant: measured latency, disk headroom, backup/restore objectives, and concurrency are the real limits.

Begin PostgreSQL migration planning when any structural trigger exists: more than one active API writer/host is required, database-level HA/failover is required, storage cannot guarantee local locking, read replicas are required, or online operations must continue through host failure. Also trigger planning when measured trends repeatedly breach agreed SLOs—for example sustained busy/locked failures after retries, write queueing drives p95/p99 beyond the service SLO, WAL checkpoints are regularly blocked by long readers, or backup/restore cannot meet RPO/RTO. Attachment growth alone should first trigger object-storage migration, not necessarily a relational-engine migration.

## 9. SQLite-to-PostgreSQL cutover

The application currently uses `Microsoft.Data.Sqlite` and SQLite SQL/PRAGMAs. PostgreSQL support is **PLANNED** and requires a repository/provider implementation, versioned PostgreSQL migrations, integration tests, deployment configuration, and a rehearsed data mover before any cutover.

### Type and behavior mapping

| SQLite source | PostgreSQL target | Conversion/validation |
|---|---|---|
| ID `TEXT` columns | `text` initially | Preserve values byte-for-byte. Adopt `uuid` only in a separate, proven migration because existing ticket IDs are readable non-UUID strings. |
| Date/time `TEXT` | `timestamptz` | Parse existing `yyyy-MM-dd HH:mm:ss` values explicitly as UTC; reject unparseable/ambiguous values. Defaults become `CURRENT_TIMESTAMP`. |
| Boolean `INTEGER` (`is_active`, `is_read`) | `boolean` | Map only `0 -> false`, `1 -> true`; reject all other values before load. |
| `INTEGER PRIMARY KEY AUTOINCREMENT` | `bigint GENERATED BY DEFAULT AS IDENTITY` | Used by `project_allocations.allocation_id` and `audit_logs.audit_id`; after loading explicit IDs, advance each identity sequence to at least the current maximum. |
| General `INTEGER` | `integer` or `bigint` | `width`/`height` fit `integer`; use `bigint` for `size_bytes`. Verify nonnegative values. |
| `BLOB` | `bytea` temporarily | For `ticket_attachments.content_blob`; preferred final model is an object key plus metadata, described below. Validate size and SHA-256. |
| JSON stored as `TEXT` | `jsonb` | Applies to `projects_json`, `tags_json`, report image JSON, text evidence JSON, and audit before/after/metadata JSON. Parse and reject invalid JSON; preserve semantic array/object shape. |
| SQLite `CHECK`/`UNIQUE`/FK | PostgreSQL constraints | Recreate every allowed-value check, uniqueness rule, severity/priority rule, tag exclusion rule, and FK. Consider named constraints; do not silently replace checks with unconstrained text. |
| `COLLATE`/string comparison defaults | explicit PostgreSQL behavior | Verify email/project uniqueness and case behavior. PostgreSQL `text` uniqueness is case-sensitive; use normalized values or `citext` only after product rules are agreed. |
| `datetime('now')` triggers | PostgreSQL trigger function | Recreate `updated_at` behavior for users, projects, and tickets with one reviewed `BEFORE UPDATE` function using UTC-aware timestamps; avoid recursive self-updates. |
| SQLite indexes with `DESC` | PostgreSQL B-tree indexes | Recreate all canonical indexes and verify query plans; do not assume identical optimizer behavior. |

All ten canonical tables must move: `users`, `user_requests`, `projects`, `project_allocations`, `bug_tickets`, `ticket_activity`, `ticket_attachments`, `audit_logs`, `auth_tokens`, and `notifications`. Load parents before children: users/projects, then requests/allocations/tickets/tokens, then activity/attachments/audit/notifications. Because `audit_logs.actor_user_id` is required, orphan checks must pass before loading.

### Cutover procedure — **PLANNED; rehearse before use**

1. **Prepare:** freeze the SQLite schema; deploy provider-neutral repository changes behind configuration; create PostgreSQL from immutable migrations; enable TLS, least-privilege roles, backups/PITR, monitoring, and connection pooling.
2. **Rehearse:** migrate a recent backup repeatedly. Record export/import duration, transformation rejects, sequence state, validation duration, and application smoke/performance results. Determine the actual outage window.
3. **Preflight:** verify SQLite integrity/FKs/JSON, PostgreSQL emptiness and schema checksum, credentials, storage, time zones (UTC), object storage, rollback backup, and operator communications.
4. **Quiesce:** put the product in maintenance/read-only mode and stop all background/API writers. Record the last SQLite transaction boundary and create a final WAL-safe `.backup`. No writes may be accepted after this point unless CDC/dual-write has been separately implemented and proven.
5. **Extract/transform/load:** use a reviewed, idempotent ETL tool—not hand-edited CSV—to stream tables in dependency order, parameterize values, parse UTC timestamps, convert booleans/JSON, and move attachment bytes. Record source IDs, row outcomes, and rejects. Run the load in transactions sized and ordered by rehearsal; never disable constraints without validating them before commit.
6. **Set identities and statistics:** advance identity sequences for allocation/audit IDs and run `ANALYZE` on loaded tables.
7. **Validate:** keep traffic closed until all checks below pass and two operators sign off.
8. **Switch:** change the API database provider/connection secret, start one canary instance, smoke test, then gradually admit traffic. Keep SQLite read-only and immutable through the rollback window.

Do not use a generic one-command conversion tool in production without examining its generated DDL and proving every conversion. Binary attachments and JSON require explicit handling.

### Cutover validation gates

- exact row count per table and exact distinct primary-key count;
- zero nulls in required target columns and zero FK/unique/check violations;
- per-table deterministic reconciliation hashes over canonical, ordered business columns, computed by a migration tool with identical UTF-8, null, timestamp, boolean, and JSON canonicalization on both sides;
- grouped counts for roles, user types, request/ticket status, severity, priority, visibility, activity kind, attachment purpose/type, notification read state, and records by day;
- minimum/maximum timestamps and identity IDs;
- every JSON value parses and expected array/object shapes match;
- attachment count, total bytes, each `size_bytes`, and each stored SHA-256 match the migrated object/byte stream;
- sampled and high-risk relationship checks: sensitive project membership, reporter/assignee/resolver access, audit actor, notification owner, and attachment owner;
- PostgreSQL indexes, constraints, trigger definitions, identity sequence next values, query plans, and slow-query baseline;
- API integration/smoke tests and authorization-negative tests, followed by observed canary error/latency checks.

A row count alone is never cutover evidence.

### Rollback strategy

The safest rollback point is before PostgreSQL accepts writes. If pre-switch validation or canary read tests fail, stop PostgreSQL use and point the application back to the untouched, read-only SQLite source.

After PostgreSQL accepts the first write, simple connection-string rollback would lose or fork data. Before cutover, choose and document one of these policies:

- **Short rollback window with no accepted PostgreSQL writes:** keep traffic read-only until sign-off, then declare PostgreSQL authoritative; or
- **Reconciled rollback:** implement and rehearse a reverse change capture/export into a newly built SQLite database, including conflicts, sequences, JSON, timestamps, and attachments. This capability does not exist today.

Without proven reverse replication, a post-write rollback must stop traffic, preserve both databases, quantify PostgreSQL-only changes, and require an incident/data-owner decision between losing those writes or performing a reviewed reconciliation. Never dual-write ad hoc. Keep the final SQLite backup, ETL manifests, rejects, checksums, and PostgreSQL backup/PITR point until the rollback window closes; then archive SQLite securely rather than immediately deleting it.

## 10. Attachment storage recommendation

Move attachment bytes out of the relational database before meaningful production growth. Use private object storage (for example S3, Azure Blob Storage, or GCS) with encryption, versioning, lifecycle/retention policy, malware/content validation, and access only through short-lived authorized application downloads. Keep relational metadata: `attachment_id`, ticket/uploader/purpose, original and safe display names, validated content type, byte size, dimensions, SHA-256, object key, object version/ETag, created timestamp, and deletion state.

Use immutable, non-guessable keys that do not contain user filenames. Upload to a temporary key, verify size/hash/type, commit metadata and final object state idempotently, and run an orphan reconciler for failures between object and database commits. Back up and restore object storage and metadata as one recovery domain; a database restore must not point at expired or deleted objects. Do not make the bucket public or persist long-lived signed URLs.

During migration, calculate SHA-256 from `content_blob`, compare it with the stored `sha256`, upload idempotently, verify by reading the object, then populate the object reference. Retain source BLOBs until all objects, restores, and downloads are validated. Removing `content_blob` requires a later versioned migration after the rollback window.

## 11. Why PostgreSQL, not MongoDB

This domain is relational: tickets reference users and projects; memberships govern sensitive authorization; attachments, activity, notifications, audit logs, requests, and tokens require enforced ownership and transactional consistency. The current schema relies on foreign keys, unique constraints, check constraints, ordered transactional status changes, joins, and auditable migrations. PostgreSQL preserves those guarantees while adding concurrent writers, mature HA/PITR, richer indexing, and `jsonb` for the few legitimately flexible fields.

MongoDB would require embedding or duplicating changing user/project data, application-managed referential integrity, multi-document transaction decisions, and new consistency logic for authorization-critical relationships. It would not solve attachment economics—large files still belong in object storage—and would make reconciliation and relational reporting harder. MongoDB could be justified for an independently owned document/event workload with different access patterns, but it is not the safe migration target for this system of record.
