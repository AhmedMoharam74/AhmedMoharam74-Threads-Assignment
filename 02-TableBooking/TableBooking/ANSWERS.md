# Assignment 02 — Table booking

Student name: Ahmed Moharam Mohamed kamal  

## What was wrong

The availability check, delay, decrement, and booked-count increment were not one atomic operation. Several threads could all observe a table as available before any of them decremented the count, resulting in overselling and inconsistent totals.

## Fix

A private lock now protects the entire check-and-update operation. The condition and both counters are read or changed under the same lock, so only five bookings can succeed and the final totals remain consistent.

## Run evidence

Original starter runs (actual results):

```text
Run 1: Booked=34, TablesLeft=-29
Run 2: Booked=35, TablesLeft=-6
Run 3: Booked=19, TablesLeft=-10
```

Fixed project runs (actual results):

```text
Run 1: Booked=5, TablesLeft=0
Run 2: Booked=5, TablesLeft=0
Run 3: Booked=5, TablesLeft=0
```

The customer IDs that win can vary; the fixed totals stayed correct on all three runs.
