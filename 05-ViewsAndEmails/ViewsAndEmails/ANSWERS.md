# Assignment 05 — Views and emails

Student name: Ahmed Moharam Mohamed kamal  

## Failures in the original

`Dictionary` was read and written concurrently, and its check-then-add-then-increment sequence could lose updates or corrupt access. `Queue.Count` followed by `Dequeue` was also a racy compound operation: workers could both see a non-empty queue and one could throw. The sent-email list was separately protected, but that did not protect the queue or view dictionary.

## Fix

The view dictionary is now a `ConcurrentDictionary` updated with `AddOrUpdate`; the email queue is a `ConcurrentQueue` consumed with the atomic `TryDequeue`; and a `ConcurrentBag` stores the completed email jobs. Producers are joined before `ProducersDone` is set, and workers exit only after producers are done and the queue is empty.

## Collection safety vs business-rule safety

A thread-safe collection makes its individual operations safe. It does not make a sequence such as “check available, then decrement” atomic, nor does it guarantee higher-level rules across multiple data structures. That rule needs a suitable atomic operation or one shared lock.

## Run evidence

Original starter run:

```text
Views expected=50000, actual=15333
Email jobs placed=20, unique sent=18, total sends=18
```

Other original runs also exposed both races directly: one terminated with `ArgumentException: An item with the same key has already been added` in `RecordView`; another terminated with `IndexOutOfRangeException` in `Queue.Enqueue` from `PlaceOrder`.

Fixed project run:

```text
Views expected=50000, actual=50000
Email jobs placed=20, unique sent=20, total sends=20
```

Two additional fixed runs produced the same `50000` view count and `20/20` unique emails.

The original run demonstrated lost view increments and missing email sends. No exception occurred in this particular run; the race can produce intermittent exceptions on other runs.
