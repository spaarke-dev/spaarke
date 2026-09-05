# ADR accuracy — re-verification with a sound method

> **Date**: 2026-09-04 · **Supersedes** the accuracy column of [`adr-classification-2026-09.md`](adr-classification-2026-09.md)
> **Why this exists**: the first pass guessed identifier names and grepped for them. That method returned "zero evidence" for ADR-047, a subsystem that is fully built. The owner caught it. Every `current` verdict from that pass was therefore unreliable, and those verdicts gate FR-08 — what CI is allowed to enforce.

---

## The method, and why it is sound

**Read each ADR for the artifacts it NAMES, then check those against the tree.** The ADR tells you what to look for; you never guess.

1. Index every file, declared symbol (`class`/`interface`/`record`/`type`), and `sprk_*` entity under `src/`, `tests/`, `scripts/` — excluding `node_modules`, `bin`, `obj`, `dist`.
2. From each ADR, extract backticked tokens that plausibly name a code artifact — a typed suffix (`*Service`, `*Store`, `*Handler`, `*Executor`, …), a file, a path, or a `sprk_*` entity. Prose in backticks (`MUST`, `null`) is deliberately excluded so it cannot register as a missing artifact.
3. Check each against the index; report found / missing per ADR.

Script: `scratchpad/verify_adrs.py`. Re-runnable; the tiers below are its output, not a judgement.

**What this method can and cannot do.** It answers *"do the things this ADR names still exist?"* It does **not** answer *"is every clause still true?"* — that needs a human read. It is a **screen**, and its value is that it is honest about which ADRs it cannot screen at all (see the 15 below).

---

## Results

| Tier | Count | Meaning |
|---|---|---|
| **ALL-PRESENT** | 18 | Every artifact the ADR names exists |
| **MOSTLY-PRESENT** (≥70%) | 8 | Misses are framework types, not our code |
| **NO-NAMED-ARTIFACTS** | **15** | **The ADR names nothing checkable — see below** |
| **PARTIAL** (30–70%) | 5 | Inspected individually |
| **MOSTLY-ABSENT** (<30%) | 3 | Inspected individually |

Every ADR in the PARTIAL and MOSTLY-ABSENT tiers was inspected by hand. **Most of the "missing" artifacts are framework types the ADR mentions in passing** — `HttpClient`, `IServiceProvider`, `AddEndpointFilter`, `GetService`, `StringBuilder`, `HttpContext`, `FluentProvider`, `GraphServiceClient`. Those are not drift.

One is the opposite of drift and worth calling out: **ADR-007's "missing" `IResourceStore` is the ADR working correctly** — the ADR says *"No generic `IResourceStore`"*, so its absence is compliance, not decay. A cruder check would have scored that as a failure.

---

## 🔴 Three genuine accuracy corrections

My first pass classified all three `current`. Two were wrong.

### 1. ADR-005 — `sprk_documentassociation` does not exist · `current` → **contested**

ADR-005 (Flat Storage in SPE) says hierarchy is represented "via Dataverse metadata and associations", and names **`sprk_documentassociation`**. That entity appears **nowhere** in the repository except inside the ADR text itself. The association entities that do exist are `sprk_userentityassociation`, `sprk_associationcount`, `sprk_associationprovenance`, `sprk_associationstatus`.

The flat-storage decision itself is sound and enforced (`SpeUploadPathIsFlatGuardTests` is green). But **the mechanism the ADR names for the hierarchy half was either renamed or never built**, and the ADR has never been updated. A reader following ADR-005 to implement hierarchy would look for a table that does not exist.

### 2. ADR-033 — names a handler shape the code does not have · `current` → **contested**

ADR-033 (Streaming chat-tool side channel) names `WorkingDocumentHandler`, `WorkingDocumentHandler.cs`, `WorkingDocumentHandlerTests.cs`, and `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WorkingDocumentTools.cs`. **None exists.**

What exists is `Services/Ai/IWorkingDocumentService.cs` + `WorkingDocumentService.cs` — the *concept* survived, under a different name, a different shape (Service, not Handler), and a different path (not under `Chat/Tools/`). Its named test file does not exist at all.

This is real drift: the ADR describes a structure that has been refactored out from under it. 7 of its 14 named artifacts are absent, and only 3 of those 7 are framework noise.

### 3. ADR-011 — stays `current`, but carries a dead path

ADR-011 names `src/client/pcf/UniversalDatasetGrid/`, which **does not exist** (the DataGrid framework superseded it; CLAUDE.md records the retirement).

**The verdict stays `current`, and that matters.** ADR-011's actual decision — *Dataset PCF for form-embedded grids, React Code Pages for standalone* — is entirely sound and still describes how the codebase works. Only an *example path* inside it is dead. Flipping the ADR to stale over a broken link would be exactly the count-proxy error this project exists to avoid.

**It is a doc-drift item, and FR-12(b) already covers it** — "broken pointer paths across `.claude/**` and `docs/**`". No new mechanism needed; this is a pre-existing example of what that check will catch on its first run.

---

## 🔴 Structural finding: 15 ADRs name nothing checkable

**ADR-002, 004, 008, 014, 016, 017, 019, 020, 023, 025, 027, 039, 040, 041, 051.**

These carry zero named artifacts — no type, no file, no path, no entity. **They cannot be verified by this method, and equally they cannot be verified by a human without interpretation**, because there is nothing concrete to check against.

This is not the same as being wrong, and several are clearly fine: ADR-002 ("plugins are not an execution runtime") is a prohibition — there is no artifact to name, and its named test enforces it anyway.

But it does explain something the classification could not. **The FR-05 accuracy axis is undecidable for roughly a third of the ADR estate**, and no amount of tooling changes that — the input isn't there. Three of the fifteen matter most:

| ADR | Usage | Why it matters |
|---|---|---|
| **ADR-039** Grounded Execution & Closed Catalogs | **858** | A heavily-cited architectural invariant with nothing concrete to check it against |
| **ADR-019** ProblemDetails | **555** | Orphaned `Proposed` **and** unverifiable — the weakest position of any ADR in the estate |
| **ADR-040** Session Ledger | **483** | Enforced by an unnamed guard, but the ADR itself names no artifact |

**Cheap, targeted remedy** (a candidate for task 011/012, not a new phase): have each of these name **one** canonical artifact — the file or type that is the decision's home. That single line converts an unverifiable ADR into a checkable one and costs a sentence per ADR. It is also what makes FR-06's routing possible: you cannot route an ADR to a mechanism when it names nothing for the mechanism to hold onto.

---

## Corrected accuracy tally

| Value | Was | Now | Change |
|---|---|---|---|
| current | 38 | **36** | −2 (ADR-005, ADR-033 → contested) |
| contested | 10 | **12** | +2 |
| stale | 1 | **1** | ADR-023 only |

**Contested (12)**: ADR-005, ADR-014, ADR-016, ADR-017, ADR-018, ADR-019, ADR-020, ADR-033, ADR-041, ADR-042, ADR-043, ADR-047.
**Stale (1)**: ADR-023 (Superseded 2026-03-19).

Note the two contested groups are now differently caused, and task 012 must treat them differently:
- **10 by ratification** — never formally accepted. Fix = ratify or withdraw.
- **2 by drift** (ADR-005, ADR-033) — accepted, but the code moved underneath them. Fix = amend the ADR to match the code, or explain the divergence.

---

## Confidence, stated honestly

**High** — the enforcement inventory (17/49, 8 named + 9 unnamed) and the ratification findings. Both come from reading status lines and test files directly, not from inference.

**Medium** — the 36 `current` verdicts. This screen proves each ADR's named artifacts exist; it does not prove every clause still holds. An ADR can name live files and still contain a rule nobody follows.

**None** — the 15 ADRs with no named artifacts. Their accuracy is unknown and this method cannot improve on that. Saying so is better than assigning them a verdict that looks like evidence.
