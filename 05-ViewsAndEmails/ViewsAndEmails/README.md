# Assignment 05 — Views and emails

**SIMULATION — Software House & Academy**

A busy SwiftBite service keeps:

1. Product page view counts in a `Dictionary`
2. Outgoing email jobs in a `Queue` drained by two background workers

Many threads use these collections at the same time.

## Run

```bash
dotnet run --project ViewsAndEmails
```

## What you should do

1. Run the project. In `ANSWERS.md`, describe the failures you see.
2. Fix the view counting path and the email job path so they stay correct under concurrency.
3. In `ANSWERS.md`, explain why making a collection safer does not automatically make the business rule safe.

## Submit

- Updated project code
- `ANSWERS.md`
