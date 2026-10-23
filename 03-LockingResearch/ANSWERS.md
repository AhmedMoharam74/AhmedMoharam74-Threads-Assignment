# Assignment 03 — Locking research

Student name: Ahmed Moharam Mohamed kamal  

## Q1. What is `System.Threading.Monitor` used for?

`Monitor` coordinates access to a shared reference object. A thread enters the monitor before a critical section and exits afterward; another thread trying to enter the same monitor waits. It also supports timed entry and condition signaling with `Wait`, `Pulse`, and `PulseAll`.

## Q2. What does C# `lock` compile to?

For a normal reference-type lock target, the compiler emits monitor entry and exit with a `try/finally`, so the monitor is released even if the body throws. Modern C# has a special path for the dedicated `System.Threading.Lock` type; the examples below use a plain `object`.

## Q3. Differences between `lock` and direct `Monitor` calls

1. `lock` automatically releases the monitor in `finally`; direct `Enter`/`Exit` requires the caller to write that pattern correctly.
2. `Monitor.TryEnter` supports a timeout or non-blocking attempt, while `lock` always waits to enter.
3. `Monitor.Wait` releases the monitor while waiting for a condition and reacquires it before returning; `Pulse`/`PulseAll` can wake waiting threads. `lock` alone has no condition-waiting syntax.

## Q4. When use `TryEnter`, `Pulse`, or `Wait`?

Use `TryEnter` when a thread should skip or retry work rather than wait indefinitely. Use `Wait` when a thread needs a condition (such as a non-empty queue) to become true while releasing the monitor. The thread that changes the condition uses `Pulse` or `PulseAll` while holding that same monitor. Always check the condition in a loop after waking because another thread may change it first.

## Q5. Equivalent critical sections

```csharp
private readonly object _gate = new();
private int _total;

void AddWithLock(int value)
{
    lock (_gate)
    {
        _total += value;
    }
}

void AddWithMonitor(int value)
{
    Monitor.Enter(_gate);
    try
    {
        _total += value;
    }
    finally
    {
        Monitor.Exit(_gate);
    }
}
```

## Sources

- [Monitor class](https://learn.microsoft.com/dotnet/api/system.threading.monitor)
- [C# `lock` statement](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock)

## Run evidence

Research-only part; no starter program is required.
