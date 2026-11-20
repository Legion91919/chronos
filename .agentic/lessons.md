# Lessons

Generic, reusable lessons only. No client names, data or secrets.
- Event sourcing: make business dates monotonic per stream; then "as of date D" is a stream prefix and point-in-time replay needs no bitemporal tricks.
- A property test needs an independent oracle (here a day-by-day interest loop vs. the segment-based aggregate); comparing a projection to the same fold proves little.
- Store the idempotency key on the event as well as in the HTTP replay cache: the cache expires, the stream does not.
