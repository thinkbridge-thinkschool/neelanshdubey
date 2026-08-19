# Day 9 - Task 2: Deadlock Reproduction and Resolution

## Setup

Run `00-setup.sql` once (either session) before running the repro or fix
scripts. It resets `Accounts` rows `1` (1000) and `2` (500) in the existing
`Day9Isolation` database, and (re)creates a minimal `Orders` table
(`OrderId INT PK, AccountId INT, Amount INT`) seeded with two rows, so the
demo has two distinct resources -- one row in `Accounts`, one row in
`Orders` -- to deadlock on.

Deadlock capture needs no extra setup: `system_health` is an Extended
Events session that ships enabled by default and already includes the
`xml_deadlock_report` event, writing to both a `ring_buffer` and an
`event_file` target. Checked with:

```sql
SELECT s.name, t.target_name
FROM sys.dm_xe_sessions s
JOIN sys.dm_xe_session_targets t ON s.address = t.event_session_address
WHERE s.name = 'system_health';
```

Both `ring_buffer` and `event_file` targets were present and running, so
trace flag 1222 was not needed.

## Repro scripts: `01-deadlock-sessionA.sql` / `01-deadlock-sessionB.sql`

A classic two-resource deadlock, each session locking the two resources in
opposite order:

| Step | Session A | Session B |
|---|---|---|
| 1 | Locks `Accounts` row (`AccountId = 1`) with an `UPDATE` | Locks `Orders` row (`OrderId = 2`) with an `UPDATE` |
| 2 | Waits 5s (`WAITFOR DELAY`), holding the lock | Waits 5s, holding the lock |
| 3 | Requests `Orders` row (`OrderId = 2`) -- **held by B** | Requests `Accounts` row (`AccountId = 1`) -- **held by A** |
| 4 | Blocks, then deadlocked | Blocks, then deadlocked |

Both scripts wrap the transaction in `TRY/CATCH` and print a timestamped
message at each step, so the terminal output shows exactly when each lock
was taken, when each request blocked, and which session lost.

## Live repro result

Session A started first, Session B ~1.5-2s later, so each session's second
lock request landed while the other was already holding the opposing lock:

```
===== SESSION A OUTPUT =====
Session A: 07:54:56 - locking Accounts row (AccountId = 1)
Session A: 07:54:56 - holding lock, waiting 5s before requesting Orders row (OrderId = 2)
Session A: 07:55:01 - requesting Orders row (OrderId = 2)
Session A: 07:55:06 - ERROR 1205: Transaction (Process ID 52) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.

===== SESSION B OUTPUT =====
Session B: 07:54:58 - locking Orders row (OrderId = 2)
Session B: 07:54:58 - holding lock, waiting 5s before requesting Accounts row (AccountId = 1)
Session B: 07:55:03 - requesting Accounts row (AccountId = 1)
Session B: 07:55:06 - COMMITTED
```

SQL Server's lock monitor detected the circular wait and killed Session A
(`spid 52`) with error 1205; Session B's transaction then completed and
committed.

## Captured deadlock graph

Extracted from the `system_health` ring buffer target and saved to
[`deadlock-graph.xml`](deadlock-graph.xml):

```sql
SELECT TOP 1 XEvent.query('.')
FROM (
    SELECT CAST(target_data AS XML) AS TargetData
    FROM sys.dm_xe_session_targets st
    JOIN sys.dm_xe_sessions s ON s.address = st.event_session_address
    WHERE s.name = 'system_health' AND st.target_name = 'ring_buffer'
) AS Data
CROSS APPLY TargetData.nodes('RingBufferTarget/event[@name="xml_deadlock_report"]') AS XEventData(XEvent)
ORDER BY XEvent.value('(@timestamp)[1]', 'datetime2') DESC;
```

(Extracted with `bcp` rather than `sqlcmd` since `sqlcmd` truncates wide
`nvarchar(max)`/XML output; `bcp` streams it in full.)

### Key details from the graph

| | Session A (spid 52) | Session B (spid 53) |
|---|---|---|
| Holds | `X` lock on `Day9Isolation.dbo.Accounts` PK | `X` lock on `Day9Isolation.dbo.Orders` PK |
| Waits for | `X` lock on `Day9Isolation.dbo.Orders` PK (held by B) | `X` lock on `Day9Isolation.dbo.Accounts` PK (held by A) |
| Isolation level | READ COMMITTED | READ COMMITTED |
| Outcome | **Chosen as deadlock victim** -- transaction rolled back | Transaction proceeded and committed |

The `<resource-list>` in the graph shows the exact circular wait:

- `keylock` on `Day9Isolation.dbo.Orders` (PK `PK__Orders__C3905BCFF2CDDF43`) -- owned in `X` mode by Session B, waited on in `X` mode by Session A.
- `keylock` on `Day9Isolation.dbo.Accounts` (PK `PK__Accounts__349DA5A6DA5955D4`) -- owned in `X` mode by Session A, waited on in `X` mode by Session B.

That is the deadlock cycle: A -> waits on Orders (held by B) -> B waits on
Accounts (held by A) -> A. SQL Server's deadlock monitor picked Session A
as the victim and rolled it back so Session B could proceed.

## Fix: `02-fix-consistent-ordering-sessionA.sql` / `02-fix-consistent-ordering-sessionB.sql`

Same two transactions, same work, but Session B's lock order was changed
to match Session A's: **both sessions now acquire `Accounts` before
`Orders`**, instead of Session B acquiring them in reverse.

| Step | Session A | Session B (fixed) |
|---|---|---|
| 1 | Locks `Accounts` row (`AccountId = 1`) | Requests `Accounts` row (`AccountId = 1`) -- **blocks**, waiting for A |
| 2 | Waits 5s, holding the lock | (still blocked) |
| 3 | Requests `Orders` row (`OrderId = 2`), commits, releases both locks | Acquires `Accounts` lock once A commits, then requests `Orders` row (now free) |
| 4 | -- | Commits |

## Live fix result

```
===== SESSION A OUTPUT (fixed) =====
Session A: 08:02:25 - locking Accounts row (AccountId = 1)
Session A: 08:02:25 - holding lock, waiting 5s before requesting Orders row (OrderId = 2)
Session A: 08:02:30 - requesting Orders row (OrderId = 2)
Session A: 08:02:30 - COMMITTED

===== SESSION B OUTPUT (fixed) =====
Session B: 08:02:27 - requesting Accounts row (AccountId = 1)
Session B: 08:02:30 - acquired Accounts lock, waiting 5s before requesting Orders row (OrderId = 2)
Session B: 08:02:35 - requesting Orders row (OrderId = 2)
Session B: 08:02:35 - COMMITTED
```

Session B simply blocked on Session A's `Accounts` lock from `08:02:27` to
`08:02:30` (~3s), then proceeded once A committed and released it. Both
sessions committed -- no error 1205, no victim.

## Why consistent lock ordering prevents deadlocks

A deadlock requires a circular wait: each transaction holds a resource the
other needs. If every transaction acquires locks in the same fixed order
(here, always `Accounts` before `Orders`), no transaction can ever hold the
second resource in the order while waiting on the first, so the wait graph
can never form a cycle -- the circular wait condition is broken, and
contention degrades to ordinary blocking instead of a deadlock.
