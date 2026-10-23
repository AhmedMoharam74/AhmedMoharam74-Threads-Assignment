# Assignment 04 — Menu views

Student name: Ahmed Moharam Mohamed kamal  

## Why Expected and Actual disagreed

`Views++` is a read-modify-write sequence, not one indivisible operation. Concurrent threads can read the same old value and overwrite one another's increments.

## What changed

Used `Interlocked.Increment` for the single integer counter and `Volatile.Read` to expose a current value after the worker threads finish.

## When would I protect this differently?

If the update had to keep several fields or a larger invariant consistent together, I would protect the whole operation with one lock. For a single numeric increment, `Interlocked` is enough and has a smaller critical section.

## Run evidence

Original starter run:

```text
Expected: 400000
Actual:   126484
```

Fixed project run:

```text
Expected: 400000
Actual:   400000
```
