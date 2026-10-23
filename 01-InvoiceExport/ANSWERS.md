# Assignment 01 — Invoice export

Student name: Ahmed Moharam Mohamed kamal  

## Problems in the original design

- Every artificial wait used `Thread.Sleep`, occupying a thread even while waiting for simulated database and file work.
- The five invoice pipelines ran one after another, so their independent order, customer-file, and invoice operations could not overlap.
- File reads and writes were synchronous.

## What changed

Kept the same flow and invoice text, but converted the simulated waits and file operations to async methods. The exporter starts the five independent pipelines first and awaits the resulting tasks one by one, as the assignment requires. Cancellation is passed through each operation.

## Run evidence

Original starter run:

```text
Starting invoice export...
Wrote invoice-1001.txt
Wrote invoice-1002.txt
Wrote invoice-1003.txt
Wrote invoice-1004.txt
Wrote invoice-1005.txt
Export finished in 5.9 s
```

Fixed project run:

```text
Starting invoice export...
Wrote invoice-1001.txt
Wrote invoice-1002.txt
Wrote invoice-1003.txt
Wrote invoice-1004.txt
Wrote invoice-1005.txt
Export finished in 1.8 s
```

Both runs produced the same five invoice files and kept the invoice content unchanged. The measured duration varies by machine.
