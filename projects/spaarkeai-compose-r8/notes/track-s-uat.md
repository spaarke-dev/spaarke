# Track S — UAT checklist (task 017)

> **Deployed** 2026-08-21 from commit `e5815e862`, both artifacts in the same window (NFR-05).
>
> | Artifact | Target | Evidence |
> |---|---|---|
> | BFF | `spaarke-bff-dev` (`rg-spaarke-dev`, `DOTNETCORE\|10.0`) | package 44.98 MB; **SHA-256 verified on 4 critical files**; `/healthz` 200; Compose + checkout routes return **401 (registered)**, not 404 |
> | `sprk_spaarkeai` | `spaarkedev1.crm.dynamics.com`, resource `5206a442-3451-f111-bec7-7ced8d1dc988` | 5,694 KB, published; **7 Track S strings + the per-document draft key verified present in the bundle** |
>
> Publish size **43.68 MB** compressed (−1.28 MB vs the 44.96 MB net10 baseline; ceiling 60 MB).
> Gates before deploy: `adr-check` 0 violations · NetArchTest 36/36 · Compose server 1,139 · Compose client 91 suites / 1,124.

---

## Why this checklist exists in this form

Every prior Compose release shipped save work and fidelity work **together**, so when UAT failed nobody
could say which half caused it. Track S ships alone specifically to make the answer unambiguous. That
only pays off if the UAT is run **row by row** against the ten failure modes. A single "yes, it saves"
result tells us nothing we did not already believe in R7 — and R7 was wrong.

**Record what you OBSERVED, not just PASS/FAIL.** The observation is the evidence; the mark is a summary
of it.

---

## The ten rows

### FR-S01 — every save failure states what happened and what to do

**Repro**: Open a Compose document. Trigger any server-side save failure available to you (easiest:
disconnect the network mid-save, or open the document in Word for the web first — see FR-S04).

**Expect**: a specific message naming the cause and a recovery, never a bare `Save failed: …` or an HTTP
code alone. Every message must also promise the edits survive — and they must actually survive.

| | |
|---|---|
| Observed | |
| PASS / FAIL | |

---

### FR-S02 — a concurrent save succeeds and WARNS (it does not refuse)

**Repro**: Two users open the same document. User A edits + saves. User B — who loaded **before** A's
save — then edits and saves.

**Expect**: B's save **succeeds**, and B sees *"Someone else saved a new version of this document while
you had it open. Your save is now the current version — use version history…"*. There must be **no
refusal loop**: B must never be stuck with unsaved work and no way forward. A's content must be present
in SPE version history.

**Watch for (owner decision B — see step 6.5 of the POML)**: if a **"Document Busy" (409)** ever appears
here, that proves Graph honors the `If-Match` precondition and closes an open question. If it never
appears across the whole UAT while concurrent saves are exercised, record that as an **open observation**
— the 409 handler may be dead code for a status the platform never returns.

| | |
|---|---|
| Observed | |
| 409 "Document Busy" seen? | YES / NO |
| PASS / FAIL | |

---

### FR-S03 — a failed save leaves the document dirty, not falsely "Saved"

**Repro**: Start a **new** document in the editor (born-in-editor, not an upload). Type something. Force
the save to fail (network off is easiest). Then: look at the Save button, press Ctrl+S, and try to close
the tab.

**Expect**: Save still **enabled**; Ctrl+S still live; the browser **warns before closing**; the toolbar
does **not** say "Saved". Reconnect and save — your text is still there and lands.

| | |
|---|---|
| Observed | |
| PASS / FAIL | |

---

### FR-S04 — a Word lock is named, and Retry works

**Repro**: Open the document in Word for the web. Leave it open. Edit + save in Compose.

**Expect**: a banner saying the document is open in Word, that it releases automatically in a few minutes,
and that your Compose changes are safe — with a working **Retry**. There must be **no "Unlock" button**
(no such thing exists). Close Word, press Retry — the save succeeds.

| | |
|---|---|
| Observed | |
| PASS / FAIL | |

---

### FR-S05 — a save cannot hang forever, and cannot double-run

**Repro (hang)**: Disconnect the network, press Save, and wait **two minutes**.
**Repro (double-run)**: Press Ctrl+S rapidly several times on a dirty document.

**Expect (hang)**: after ~2 minutes the save reports it took too long and stopped; the editor is usable
again **without a page reload**; it does not sit on "Saving…" forever.
**Expect (double-run)**: exactly **one** save happens. No duplicate version in history.

| | |
|---|---|
| Observed | |
| PASS / FAIL | |

---

### FR-S06 — a failed write can never present as "Saved ✓"

**Repro**: This one is hard to force by hand — it is the storage/record-failure path. If it cannot be
forced, mark **N/A (covered by seam test)** and say so; the seam tests cover it
(`ConcurrencySaveSeamTests`, `partially-recorded` + `storage-failed` on HTTP 200).

**Expect if it does occur**: the banner says plainly that the document was **not** stored (or was only
partly recorded), never "Saved".

| | |
|---|---|
| Observed | |
| PASS / FAIL / N/A | |

---

### FR-S07 — a save can never overwrite a newer version with pre-edit content

**Repro**: Not forceable by hand (it requires an SPE download failure mid-save). Mark **N/A (covered by
seam test)** — `Save_StaleBase_ReanchorDownloadFails_WritesNothing_RefusesStale` proves nothing is
written and a 409 is returned.

**What to watch for instead during general use**: any case where saving **loses** somebody else's recent
content silently. That is the failure this requirement exists to prevent, and it would be a FAIL.

| | |
|---|---|
| Observed | |
| PASS / FAIL / N/A | |

---

### FR-S08 — large documents

**Repro**: Save a document **over 4 MB** (this used to fail outright). Then, if you have one, a document
over **25 MB**.

**Expect**: the >4 MB document **saves normally**. The >25 MB one is refused with a message **naming the
limit** ("Compose can save documents up to 25 MB…") and stating your changes are still here — never a
blank failure, never a raw browser error.

**Note (ratified 2026-08-21)**: Compose deliberately does **not** route through the chunked-upload path.
Graph's simple upload has been 250 MB since Oct 2023, and the chunked path cannot carry the end-to-end
`If-Match` that FR-S02 depends on. See `notes/document-size-ceilings.md`.

| | |
|---|---|
| >4 MB observed | |
| >25 MB observed | |
| PASS / FAIL | |

---

### FR-S09 — the honest-failure set (nine sub-rows)

Each of these was previously **silent** or **misreported**. Check the ones you can reach.

| # | Repro | Expect |
|---|---|---|
| 1 | Press Save immediately after the editor mounts / in an odd state | Either it saves, or it **says** why. Never nothing. |
| 2 | Press **Save As**, then dismiss the name dialog (Esc / Cancel) | *"Not saved — this document needs a name."* The document stays dirty. |
| 3 | — (config-level; not reachable in a healthy environment) | Save is disabled **with a tooltip** stating why, not silently grey |
| 4 | Open the same document in a second tab (same user) | The conflict dialog appears; **"Force-close other session" WORKS** — it must not report failure. If another **user** holds it, their **name** is shown. |
| 5 | Not forceable by hand (Dataverse must fail mid-save) | N/A — seam-tested |
| 6 | Not forceable by hand (Graph must throttle) | N/A — seam-tested. If it happens: *"the document service is busy… try again in about N seconds"*, **never** a 500 |
| 7 | Save a document, then change its size (add pages) and save again. Check the Documents grid | The file **size** shown updates. It used to keep the first version's size forever. |
| 8 | Open **two different** Compose documents, edit both without saving, close both, reopen the first | The first document's unsaved draft is **still recovered**. It used to be destroyed by the second. |
| 9 | Open a document you lack permission for, or one that was deleted | *"You do not have permission…"* / *"Document not found. It may have been deleted or moved."* — not `HTTP 403`/`HTTP 404` |

| | |
|---|---|
| Observed (per sub-row) | |
| PASS / FAIL | |

---

### FR-S10 — save failures are visible without asking you

**Repro**: After the UAT, check App Insights:

```kusto
customMetrics
| where name == "compose.save_outcomes"
| summarize sum(value) by bin(timestamp, 5m), tostring(customDimensions["outcome"]), tostring(customDimensions["cause"])
```

**Expect**: rows for every save you made during UAT, with the outcome and cause you'd expect from what you
saw. A `persisted` count that matches your successful saves; any failure you triggered visible as its own
outcome + cause.

**This is the row that means we never need a UAT to find out saving is broken again.**

| | |
|---|---|
| Observed | |
| PASS / FAIL | |

---

## Result

| | |
|---|---|
| Rows PASS | |
| Rows FAIL | |
| Rows N/A (seam-covered) | |
| **GO / NO-GO for Phase 2** | |

**GO** means "can't save" is closed and the fidelity architecture work (Phase 2 — oracle + corpus) can
begin. **NO-GO** means a Track S failure mode is still live; file it as a follow-up against the owning
task (010–016) rather than starting Phase 2 on top of it.

### Open observations (not failures)

- **`If-Match` on `PUT …/content` is undocumented** in the Graph v1.0 reference. FR-S02's row records
  whether a 409 was ever seen. See task 017 step 6.5.
- **Metadata refresh writes unconditionally** on every replace save (correct, but adds a Dataverse audit
  entry per save). Follow-up: make it conditional by selecting the two columns in the alt-key read that
  already runs. Raised at code review 2026-08-21; deliberately not changed pre-deploy.
