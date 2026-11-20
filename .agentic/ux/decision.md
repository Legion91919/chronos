# Sketch decision: loan position screen

Reviewed at 1440px and 390px (the layouts collapse to one column under 700px).

| Variant | Main job speed | Clarity of numbers | Accessibility | Fits tone | Total |
|---|---|---|---|---|---|
| A: dense table (loan list + statement) | 5 | 4 | 4 | 5 | 18 |
| B: summary cards | 3 | 5 | 4 | 4 | 16 |
| C: sign-off queue | 3 | 3 | 4 | 3 | 13 |

**Pick: A, with B's summary strip and C's sign-off panel.** Operations live in the loan list and statement (A).
The four-number strip from B shows outstanding, principal, interest and pending at a glance. The "awaiting approval"
panel from C keeps pending drawdowns in view and clearly out of the balance. The "As of" date control (A) drives
point-in-time replay. Pending is shown with a label and an amber outline, never by colour alone.
