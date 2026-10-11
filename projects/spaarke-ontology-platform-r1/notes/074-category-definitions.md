# Task 074 - triage categories, as the classifier sees them

> **Read live from `spaarkedev1` on 2026-10-08 01:43 UTC**, with the production guidance query (`ScopeResolverService.BuildLookupGuidanceUrl`
> plus the task 072 filter): `sprk_triagecategories?$select=sprk_name,sprk_classifierguidance&$orderby=sprk_name asc&$top=200&$filter=statecode eq 0 and (sprk_enabled eq true)`.
> 10 active, enabled rows. The order and text below are what the prompt's `## Allowed values for 'category'` section lists (guidance flattened to one line; no row exceeds the 1000-char cap).

## How to label (owner)

- For each item in `074-labelling-set.json`, choose **exactly one** category name from the list below, using **only the email text** and these definitions.
- If you genuinely cannot decide, label it `AMBIGUOUS`. Per the D-64 protocol, ambiguous items are excluded from the recall denominator and the excluded count is reported. Do **not** use `Unclassified` for this; `Unclassified` is a category the classifier can choose.
- Record labels as `{ "id": "L001", "label": "<category name>" }`, or as a simple `L001 = <name>` list. Labels are final once the classifier runs; nothing is relabelled afterwards.
- Please do not look at `074-drafting-intent.json` before labelling. It holds the drafter's intended mix, sealed for the composition check.

## Categories (verbatim live guidance)

### Administrative

Routine operational correspondence with no legal substance and no financial or deadline consequence: confirmations, acknowledgements, receipt notices, contact or address changes, access requests, and system messages. If the message asserts an obligation, a deadline, or a cost, choose a more specific category.

### Client instruction

The client directs, authorises, or changes the mandate: instructions to proceed, hold, settle, escalate, or change strategy. Use this for direction the firm is expected to act on. If the direction is specifically about the cost or quantity of work, prefer Scope / budget change; if about rates or billing terms, prefer Fee / rate change.

### Court / Filing

Correspondence from or about a court, tribunal, arbitrator, or regulator: orders, notices, filings, service of process, hearing dates, and procedural deadlines. Choose this whenever a court-imposed date or requirement is present, even when other topics also appear in the message, because the externally imposed deadline dominates.

### Fee / rate change

The PRICE of work is being changed or proposed: hourly rate increases, new or substituted timekeepers billing at different rates, changes to discounts, fee arrangements, or billing terms. This is about the rate per unit of work, not the quantity of work. If the quantity or scope of work is what is changing, use Scope / budget change. If an invoice already reflects the amount, use Invoice / Billing.

### Invoice / Billing

A bill or billing record already exists: an invoice is attached or referenced, a payment or remittance is discussed, or a specific billing entry is queried, disputed, or corrected. This category is about money ALREADY billed. If the email instead proposes future cost that has not been billed yet, use Fee / rate change (price) or Scope / budget change (quantity).

### Marketing / Noise

Unsolicited or bulk correspondence with no bearing on any matter: vendor pitches, newsletters, event and webinar invitations, automated promotions, and mailing-list traffic. Nothing is required of the recipient. Do not use this for unsolicited mail that nonetheless concerns a live matter.

### Opposing counsel

Correspondence from or with the opposing party's counsel: negotiation, demands, settlement positions, discovery disputes, and adversarial positions taken. This category identifies the source and the adversarial posture of the message. If the message carries a court-imposed deadline, prefer Court / Filing.

### Scheduling

Arranging or changing the timing of meetings, calls, depositions, interviews, or availability, with no substantive legal content of its own. If the date being discussed was set by a court or tribunal, use Court / Filing instead.

### Scope / budget change

The AMOUNT of work is being changed or proposed, or a budget revision is requested: additional depositions, experts, workstreams or phases; an estimate of cost beyond the current budget; a request to increase or re-baseline a budget. Use this for a commitment about FUTURE cost that the budget does not yet reflect, whether or not a dollar figure is given. If a bill already exists for the work, use Invoice / Billing instead. If the rate per hour is what is changing rather than the quantity of work, use Fee / rate change.

### Unclassified

Use this ONLY when no other category fits the message, or when the message is too ambiguous to classify with confidence. This is an explicit abstention, not a default: choosing it is a signal that a human should look and that the taxonomy may need a new row. Never choose Unclassified merely because a message spans two categories -- pick the dominant one using the tie-breakers in the other rows.

## The prompt preamble the classifier gets with this list

> Choose exactly one. Emit only the name before the dash, exactly as written; the text after it describes when to use it.
