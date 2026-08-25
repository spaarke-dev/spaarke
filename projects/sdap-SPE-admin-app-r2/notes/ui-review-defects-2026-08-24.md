# Two defects found by walking the UI — neither owned by any task

> 2026-08-24 · Found during the nine-screen review that design §9's first acceptance criterion
> required and that had never been run. Both fixed the same day. Neither appears in any POML.

Worth stating plainly: **both were found by using the app, not by reading code.** Five tasks had
touched the container-types screen and none surfaced either. That is the same lesson task 011 taught
(three of four container-type ops were app-only, guaranteed 403, after five tasks in that file).

---

## Defect 1 — "Manage Permissions" silently landed on the Dashboard

**Symptom.** In Search results, selecting an item and choosing *Manage Permissions* opened a new tab
— which showed the **Dashboard**. Every time.

**Cause.** [`ItemResultsGrid.tsx:552`](../../../src/solutions/SpeAdminApp/src/components/search/ItemResultsGrid.tsx#L552)
builds a deep link:

```ts
url.searchParams.set("page", "containers");
url.searchParams.set("containerId", containerId);
window.open(url.toString(), "_blank");
```

…and **nothing ever read either parameter.** `App.tsx` parsed only `configId` / `buId`, and only out
of the Dataverse `data` bag — while `activePage` was hard-initialised to `"dashboard"`.

This is the project's signature shape again: an upper layer reading a dropped value as a benign
default. And it was well camouflaged — **a tab did open**, so the action looked like it worked. The
only way to notice is to read what the new tab actually shows.

**Fix.**
- `SpeAdminParams` gains `page` + `containerId`, read from the **top-level query string first, then
  the `data` bag**. Reading only `data` is exactly why in-app deep links did nothing: Dataverse packs
  launch params into `data`, but in-app links build plain query params on the current URL.
- `page` is narrowed through `toSpeAdminPage()` against a literal list. An unrecognised value falls
  back to Dashboard rather than being cast through — a bad hand-edited `?page=` is not worth a blank
  screen.
- `activePage` seeds from `params.page`.
- `ContainersPage` gains `initialDetailContainerId`, applied as **initial state, not a controlled
  prop** — controlled would re-open the panel every time the user closed it, since the URL still
  names the container.

---

## Defect 2 — an expired trial container type was invisible

**Symptom.** The live tenant holds a trial container type that expired **2025-10-10 — eleven months
ago**. The app said so nowhere.

**Cause.** `expiryDateTime` was rendered in exactly **one** place —
`ContainerTypeDetail.tsx`, as a plain (red) date. A date alone cannot say whether it has *passed*, so
an expired trial rendered identically to one expiring next year. The grid had no expiry column at all.

Task 030 did add a 30-day expiry warning — **to the create dialog**, which warns the only audience
that cannot yet be affected by it.

**Why it matters.** Per `knowledge/sharepoint-embedded/docs/learn-containertypes.md`, a trial
container type is valid for 30 days, is **not renewable**, and simply stops working afterwards. An
admin scanning a grid of container types had no way to tell a working type from a dead one.

**A stale comment was part of the cause.** `ContainerTypesPage.tsx` carried:

> *"It does NOT recover `owningAppId`, `azureTenantId`, or `expiryDateTime` — the BFF never sends
> those."*

True when written; **task 030 falsified two thirds of it the same day.** It read as documentation
that the data was unavailable. Now corrected to name `azureTenantId` as the only remaining gap.

> Third instance in this project of a confidently-worded comment outliving its truth — after the
> `Graph SDK 5.101.0` comment that kept `billingClassification` null for ten days, and
> `AuditLogEndpoints.cs:159`'s lookup-GUID quoting claim. **A comment asserting what another layer
> does is a claim with an expiry date.**

**Fix.** `assessTrialExpiry(ct, now)` added to `containerTypeLifecycle.ts` — the same pure-data module
`assessBilling` lives in, so it is deterministic and asserts nothing about presentation.

- Five states: `not-a-trial` · `unknown` · `live` · `expiring` · `expired`.
- **`unknown` never collapses to `live`.** Graph does not always return `expirationDateTime`, and a
  trial whose date we cannot read is not a trial we know is healthy.
- **`not-a-trial` is only claimed when the classification actually says so** — an unknown
  classification is not evidence of absence.
- `now` is **injected**, never read from the clock inside the function.
- Surfaced in three places: a **Trial Expiry grid column**, a **page-level MessageBar** (`error`
  intent when something has already expired — a live outage, not a deadline), and the **detail panel**
  badge + consequence next to the date.

---

## ⚠️ Not covered by tests — and it cannot be, today

`assessTrialExpiry` is pure, deterministic, and exactly the shape ADR-038 calls unit-testable domain
logic. **SpeAdminApp has no test runner at all** — no vitest, no jest, and its `lint` script invokes
an ESLint that is not installed. So the client's entire logic surface (lifecycle assessments, billing
assessment, the overridables parser, the compliance module) is unverified by anything but `tsc`.

This is now the second measurement gap recorded against this code page. Standing up vitest is its own
task and was **not** done here — it would widen two small defect fixes into tooling work. Recorded so
it is chosen rather than defaulted into.

---

## Gates

| Gate | Result |
|---|---|
| `tsc --noEmit` on touched files | 0 new errors (2 pre-existing in `App.tsx` predate this change) |
| Code page build | ✅ 2,351.57 kB |
| BFF | untouched — both fixes are client-only |
