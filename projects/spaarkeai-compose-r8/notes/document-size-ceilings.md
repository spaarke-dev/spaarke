# Task 015 — Document size ceilings (FR-S08)

> **PARTIAL — server half landed 2026-08-20 (`06b370995`). The client pre-flight is NOT done.**
> Read "What remains" before continuing.

---

## Two ceilings, neither of which told the user anything

| Ceiling | Where | Effect |
|---|---|---|
| **4 MB** | `UploadSessionManager.UploadSmallAsUserAsync` threw `ArgumentException` | a create-on-save of ANY document over 4 MB failed outright |
| **~22 MB** | Kestrel's 30 MB default request-body cap vs a base64+JSON envelope (4/3 inflation) | rejected at the TRANSPORT layer, before any handler ran — no body, no message |

The second one explains the "~22 MB" figure in the design: it is 30 MB ÷ 1.333.

## The 4 MB number was stale by nearly two years

Graph's simple upload (`PUT .../content`) has accepted **250 MB since October 2023** — the docs went
4 MB → 25 MB → 256 MB → 250 MB across three commits that month. The 4 MB figure survives in the
*retired* OneDrive REST docs, which now redirect to the Graph page carrying the new number. SharePoint
Embedded documents the same 250 MB simple-upload boundary for containers, and SPE's own per-file limit
is 250 GB.

So the guard was enforcing a platform limit that no longer exists, and its advice — "use chunked upload
instead" — sent callers to a resumable session they do not need.

## Deviation from the POML — stated for review

The POML's premise is "the chunked upload exists, Compose is not using it — route Compose to it", and
its escalation trigger says to **stop** if the chunked path cannot carry `If-Match`.

**It cannot.** Verified against Microsoft Learn:

- `createUploadSession` accepts `If-Match`, but only at **session creation** — nothing re-checks at commit.
- `driveItemUploadableProperties` has no eTag/cTag field at all.
- The chunk `PUT`s go to a preauthenticated `uploadUrl` that accepts **no** precondition (and rejects an
  `Authorization` header).
- `@microsoft.graph.conflictBehavior: "fail"` is a **filename** collision check (`409 nameAlreadyExists`),
  not a version check.

So routing there would have silently weakened the concurrency guarantee task 011 established — exactly
the "correctness regression disguised as a size fix" the trigger names.

**But the trade does not have to be made.** With simple upload at 250 MB, the chunked path is
unnecessary for any document Compose will ever carry. The guarantee is preserved *and* the ceiling is
removed. That is a Path-C resolution (comply with the invariant by a different route), not an accepted
deviation from it — but it **does** contradict the POML's acceptance criterion "a ≥4 MB first save
succeeds through the EXISTING chunked-upload path", which is why it is flagged here and in the commit
message rather than quietly satisfied. **The owner should ratify this at review.**

## What landed (server)

- **`ComposeSaveLimits`** (in `IComposeService.cs`) — the single source. `MaxDocumentBytes` = 25 MB,
  matching the established Spaarke ceiling (`DocumentUploadWizard`, `OfficeService`, chat attachments —
  a document the user can upload through the Documents pane must not be one Compose refuses to save).
  `MaxRequestBodyBytes` = 1.5 × that, so the transport cannot pre-empt the honest refusal.
  `MaxDocumentDisplay` is the one formatting site, so message and enforcement cannot disagree.
- **The 4 MB guard deleted** from `UploadSmallAsUserAsync`, with the platform history recorded inline.
  Deliberately NOT replaced with a 250 MB guard — a second threshold is the divergence this task exists
  to remove; the caller that cares enforces its own product limit.
- **The oversize refusal** on the shared `ExecuteSaveAsync` path (so replace and create-on-save cannot
  diverge), before any render or byte transfer → `refused-invalid` + telemetry cause `too-large` + a
  ProblemDetails naming the actual limit.
- **`RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes)`** on both save routes.

### Verification

- `ConcurrencySaveSeamTests` **9/9** — new: an oversize document is refused with the stated limit and
  **nothing is written** (both `ReplaceFileContentAsUserAsync` overloads watched); a **6 MB** document
  saves and actually reaches storage.
- All Compose tests: **1135/1135**.
- Publish **43.68 MB** compressed incl. PDBs — **0.00 MB delta**. No vulnerable packages. No new NuGet.
- Note for whoever touches the 6 MB fixture: it requests **12 MB** of GUID-hex filler because the OPC
  zip deflates it by about half. The assertion on the packaged length is what holds the contract — do
  not "simplify" it away.

## What remains (client)

The client pre-flight: measure before sending, refuse over the limit with the number stated.

**The design decision is already made and is the reason it was not rushed**: the client must NOT carry a
compiled-in copy of the limit — two constants is precisely how "your file is fine" becomes an
unexplained failure. The server should advertise `maxDocumentBytes` on the Load (and Upload) response;
the client stores it and pre-flights against it. When the server does not advertise one (older BFF, or a
Browse-only mount that never called Load), the client does **no numeric pre-flight** and lets the server
refuse honestly — guessing is what the constraint forbids.

Concretely:
1. Add `maxDocumentBytes` to `LoadComposeDocumentResponse` (and the upload response) sourced from
   `ComposeSaveLimits.MaxDocumentBytes`.
2. Carry it into `ComposeWorkspace` reducer state at mount.
3. In `triggerSave`, before building the request: if the limit is known and the payload exceeds it,
   dispatch `saveFailed` with the stated number and do not send.
4. Client test: over-limit → no fetch, message names the limit; under-limit → unchanged; limit absent →
   no pre-flight, save proceeds.

`ComposeWorkspace.tsx` is also task 016's file — do 015's client half and 016 in one pass, or 015 first.

## Finding for the owner — `If-Match` on `PUT .../content` is UNDOCUMENTED

Task 011's concurrency guarantee rests on sending `If-Match` on the content PUT. Of the v1.0 driveItem
APIs, exactly four document an `if-match` header: `delete`, `update`, `move`, `createUploadSession`.
**`put-content` is not among them** — its headers table lists only `Authorization` and `Content-Type`,
and it never had one.

So the header we send may be honored, silently ignored, or changed without notice, and no SPE doc
mentions preconditions at all. This is not a task-015 problem to solve, but it is load-bearing for
task 011's claim and worth an **empirical probe** before Track S deploys (task 017): PUT with a
deliberately stale `If-Match` against a real SPE container as the user, and see whether it returns 412
or silently overwrites. One test settles it.

Related: `knowledge/sharepoint-embedded/NOTES.md` (~line 407) still carries a stale "documents >4 MB"
TODO encoding the old 4 MB figure. Worth correcting when someone is next in that file.
