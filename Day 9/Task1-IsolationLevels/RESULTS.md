# Day 9 - Task 1: Captured Anomaly Test Results

Live results from running the dirty read, non-repeatable read, and phantom read
demos against `day9-sql` (SQL Server 2022, `localhost,1434`, DB
`Day9Isolation`), with Session A run as a background `sqlcmd` process and
Session B run in the foreground so both sessions' output landed in one
terminal capture. The `Accounts` table was reset to `(1,1000),(2,500)` before
each anomaly.

## 1. Dirty read

**Setup:** Session A opens a transaction, sets `Balance = Balance - 100` for
`AccountId = 1` (900), waits, then rolls back.

**PERMISSIVE - Session B on READ UNCOMMITTED:**
Session B's `SELECT` ran while Session A's transaction was still open and
returned the uncommitted value:

```
AccountId   Balance
----------- -----------
          1         900
```

Balance 900 was never actually committed - Session A rolled back right after,
restoring `AccountId 1` to 1000. Session B read data that never existed as a
committed state: a dirty read.

**PREVENTED - Session B on READ COMMITTED (default):**
Same scenario, but Session B's `SELECT` blocked on Session A's exclusive lock
instead of reading the uncommitted value:

```
Time before Session B query: 11:15:37
...
Time after Session B query:  11:15:41
```

~4 seconds of blocking. Session B's query only returned once Session A rolled
back, and it read `Balance = 1000` - the last committed value, never the
in-flight 900.

## 2. Non-repeatable read

**Setup:** Session A reads `Balance` for `AccountId = 2` twice inside one
transaction, 6 seconds apart. Session B updates `Balance = Balance + 50` for
the same row (autocommit, no explicit transaction) in between A's two reads.

**PERMISSIVE - Session A on READ COMMITTED:**

```
Session A: first read:
Balance
-----------
        500

Session A: second read:
Balance
-----------
        550
```

Session B's update committed at `11:22:38`, in between Session A's two reads.
The same row changed value mid-transaction - a non-repeatable read.

**PREVENTED - Session A on REPEATABLE READ:**

```
Session A: first read:
Balance
-----------
        550

Session A: second read:
Balance
-----------
        550
```

```
Time before Session B update: 11:22:44
Time after Session B update:  11:22:48
```

~4 seconds of blocking. Session A's shared lock on the row held for the life
of its transaction, so Session B's `UPDATE` blocked until Session A committed.
Both of Session A's reads returned the identical value.

## 3. Phantom read

**Setup:** Session A runs `SELECT ... WHERE Balance > 0` twice inside one
transaction, 6 seconds apart. Session B inserts a new row matching that
predicate (autocommit) in between A's two reads.

**PERMISSIVE - Session A on REPEATABLE READ:**

```
Session A: first read:
AccountId   Balance
----------- -----------
          1        1000
          2         500
(2 rows affected)

Session A: second read:
AccountId   Balance
----------- -----------
          1        1000
          2         500
          3         250
(3 rows affected)
```

Session B's `INSERT` for `AccountId 3` committed at `11:25:59`, in between
Session A's two reads. REPEATABLE READ locks rows already read but not the
range itself, so the new row was visible on the second read - a phantom row.

**PREVENTED - Session A on SERIALIZABLE:**

```
Session A: first read:
AccountId   Balance
----------- -----------
          1        1000
          2         500
          3         250
(3 rows affected)

Session A: second read:
AccountId   Balance
----------- -----------
          1        1000
          2         500
          3         250
(3 rows affected)
```

```
Time before Session B insert: 11:26:06
Time after Session B insert:  11:26:10
```

~4 seconds of blocking. SERIALIZABLE takes a range lock covering
`Balance > 0`, so Session B's `INSERT` of `AccountId 4` blocked until Session
A committed. Both of Session A's range reads returned identical rows.

## Summary: anomaly -> lowest isolation level that prevents it

| Anomaly | Lowest isolation level that prevents it | Evidence captured |
|---|---|---|
| Dirty read | READ COMMITTED | Session B blocked ~4s (11:15:37 -> 11:15:41), read only the committed `Balance = 1000` |
| Non-repeatable read | REPEATABLE READ | Session B blocked ~4s (11:22:44 -> 11:22:48), Session A's two reads both returned `550` |
| Phantom read | SERIALIZABLE | Session B blocked ~4s (11:26:06 -> 11:26:10), Session A's two range reads both returned the same 3 rows |
