# Day 9 - Task 1: Isolation Levels and Read Anomalies

## Anomaly -> lowest isolation level that prevents it

| Anomaly | Lowest isolation level that prevents it |
|---|---|
| Dirty read | READ COMMITTED |
| Non-repeatable read | REPEATABLE READ |
| Phantom read | SERIALIZABLE |

## Setup

Run `00-setup.sql` once (either session tab) before running any anomaly
script. It rebuilds the `Day9Isolation` database with a single `Accounts`
table seeded with `(1, 1000), (2, 500)`.

## Scripts

Each anomaly has a Session A / Session B pair. Both files contain two parts:
**PART 1** reproduces the anomaly at the isolation level that allows it,
**PART 2** repeats the same scenario at the isolation level that blocks it.
Open two query tabs, both connected to `localhost,1434` (SA / `YourStrong!Passw0rd`),
and run Session A and Session B side by side, alternating as each script's
`WAITFOR DELAY` comments direct.

| Pair | Description |
|---|---|
| `01-dirty-read-sessionA.sql` / `01-dirty-read-sessionB.sql` | Session A updates `Accounts.Balance` (AccountId 1) inside an open transaction and rolls back. PART 1: Session B on READ UNCOMMITTED reads the uncommitted value (dirty read). PART 2: Session B on READ COMMITTED blocks instead of reading it. |
| `02-nonrepeatable-read-sessionA.sql` / `02-nonrepeatable-read-sessionB.sql` | Session A reads `Accounts.Balance` (AccountId 2) twice inside one transaction while Session B updates and commits in between. PART 1: Session A on READ COMMITTED sees two different values for the same row. PART 2: Session A on REPEATABLE READ sees the same value both times because Session B's update blocks. |
| `03-phantom-read-sessionA.sql` / `03-phantom-read-sessionB.sql` | Session A runs the same range query (`WHERE Balance > 0`) twice inside one transaction while Session B inserts a new matching row in between. PART 1: Session A on REPEATABLE READ sees an extra row on the second read (phantom). PART 2: Session A on SERIALIZABLE sees identical results both times because Session B's insert blocks. |

### How to run each pair

1. Open two query tabs/sessions, both connected to `localhost,1434`.
2. In tab 1, open the `...sessionA.sql` file; in tab 2, open the matching `...sessionB.sql` file.
3. Run Session A's PART 1 batch first -- it starts a transaction and then pauses on `WAITFOR DELAY '00:00:10'`.
4. While Session A is paused, run Session B's PART 1 batch and observe the result noted in its comment.
5. Let Session A's PART 1 finish (rollback/commit), then repeat steps 3-4 with each script's PART 2 batch to see the same scenario blocked/prevented at the higher isolation level.
