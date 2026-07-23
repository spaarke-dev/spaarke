# Task 041 — Form Component Control (FCC) registration + standard-form retention plan

> **Status**: DOCUMENTED (not executed). The Dataverse form customization cannot be
> performed in this sandbox. This note is the executable plan for **task 043 (deploy)**.
> **Reference**: `reference/r3-send-side-design.md` §7.4 (Surface 2), §7.2, §5.10; ADR-026; FR-16.
> **Reversibility (NFR-07 / design decision 2026-06-05)**: the standard OOB
> `sprk_communication` main form is **RETAINED as a hidden / admin-only fallback — NOT deleted**.

---

## 1. What ships in this task (041) vs. what deploys later (043)

| Artifact | Task | State after 041 |
|---|---|---|
| `<SendEmailPage />` mounted in the Code Page email branch | 041 | ✅ Done (`EmailComposerSlot.tsx`) |
| Single self-contained web resource `sprk_communicationpage.html` | 040/041 | ✅ Built via `npm run build:prod` → `out/sprk_communicationpage.html` |
| Web resource **uploaded** to Dataverse (`sprk_communicationpage`, type Webpage/HTML) | 043 | ⏳ Deploy step |
| FCC-hosting main form registered as default | 043 | ⏳ This plan |
| Standard OOB form hidden + retained as admin fallback | 043 | ⏳ This plan |

Task 041 lands the **wiring + this registration plan**. No Dataverse metadata is mutated here.

---

## 2. Surfaces (reference §7.4)

- **Surface 1 — Ribbon "+ New Email"**: launches the Code Page via
  `Xrm.Navigation.navigateTo({ pageType:"webresource", webresourceName:"sprk_communicationpage", data:"mode=compose[&associatedTo=…]" }, …)`. Ribbon work is separate (not this task).
- **Surface 2 — Form Component Control (THIS note)**: navigating to an existing
  `sprk_communication` record opens the Code Page (`mode=view&id={recordGuid}`) instead of
  the auto-generated Dataverse form. **Higher-risk** — it changes default record-open behavior.
- **Surface 3 — Embeddable launch from other Code Pages/components**: opportunistic, no central work.

---

## 3. FCC registration approach (Surface 2)

The Code Page is hosted inside a Dataverse **main form** via a **Form Component Control**
(the `MscrmControls.ContainerControl.CustomControl` / web-resource host pattern) bound to a
container so the whole form region renders the `sprk_communicationpage` HTML web resource.

Two equivalent registration mechanisms — pick per environment tooling at deploy time:

1. **Custom Page / web-resource-hosting form control** — add a Form Component / web-resource
   control to a new main form and set it to host `sprk_communicationpage`, passing the record id
   through so the page bootstraps with `mode=view&id={recordGuid}` (the page's `parseParams`
   already reads `id` from the `data=` contract; when hosted on the form the record context id is
   supplied by the host and mapped to the `id` URL param).
2. **FormXml edit** — unpack the `sprk_communication` form, insert the web-resource host control
   cell bound to `sprk_communicationpage`, set it as the primary form, re-pack + import.

Whichever is used, the **default user-visible main form** for `sprk_communication` becomes the
FCC-hosted Code Page. The URL contract (`mode`, `id`, `associatedTo`) is unchanged (§7.3).

### Mode mapping on the FCC surface
- Opening an existing record → `mode=view` (read-only composer; Reply / Forward / Edit-draft
  buttons re-navigate with `?mode=reply|forward|draft&id={id}` per §7.5).
- New record from the grid/ribbon → `mode=compose` (Surface 1).

---

## 4. Standard-form retention plan (REVERSIBILITY — binding)

The existing standard OOB `sprk_communication` main form **MUST be retained**, not deleted:

1. **Do NOT delete** the current standard main form.
2. **Reorder** forms so the new FCC-hosting form is the **default** (top of the form order).
3. **Restrict** the standard form's roles to **System Administrator / System Customizer only**
   (hidden from end-user security roles) — it remains reachable via the form selector for
   admin / data-sheet inspection / debugging.
4. Keep the standard form **published** (hidden ≠ deleted) so a rollback is a pure form-order +
   role-visibility flip with no re-creation.

### Rollback (escape hatch)
If the FCC swap misbehaves: reorder the standard form back to default and restore its role
visibility. No data migration, no form re-creation. This is why the exact identifiers below
must be captured at deploy time.

---

## 5. Identifiers to capture at deploy time (task 043)

Record these in this table when 043 runs against the target environment (values are
environment-specific and cannot be resolved in the sandbox):

| Item | Logical / name | GUID | Notes |
|---|---|---|---|
| Entity | `sprk_communication` | `<capture>` | Target table |
| Standard OOB main form (RETAIN) | `<form name>` | `<formid GUID>` | Set admin-only, keep published |
| New FCC-hosting main form (DEFAULT) | `sprk_communication Email (Code Page)` (proposed) | `<formid GUID>` | Hosts `sprk_communicationpage` |
| Web resource | `sprk_communicationpage` | `<webresourceid GUID>` | Type: Webpage (HTML); uploaded in 043 |
| Solution | `<solution unique name>` | — | Carries form + web resource |

> Capture the standard form's `formid` **before** changing form order so rollback is deterministic.

---

## 6. Smoke observations feeding task 043 UI tests

- All five URL modes route to the correct composer chrome (`compose`→"New Email",
  `view`→"Email" read-only, `reply`→"Reply", `forward`→"Forward", `draft`→"Edit Draft") —
  the wrapper forwards `mode` straight to the engine (`initialState`/header switch).
- **Fidelity follow-up for 043**: the `SendEmailPage` wrapper (task 021) accepts `communicationId`
  + pre-fill (`initialTo`/`initialSubject`/`initialBody`) but **not** a full `sourceRecord`. So on
  the FCC `view`/`reply`/`forward`/`draft` surface the composer currently hydrates To/Subject/Body
  from URL pre-fill (and the layout's record→subject/body fallback), **not** from a mapped
  `ISourceCommunicationRecord`. Reply/forward subject-prefix (`Re:`/`Fwd:`) and forwarded-body
  wrapping only fire when a `sourceRecord` is supplied. If 043 requires full record hydration on the
  FCC surface, extend the wrapper contract (task-021 surface) to accept `sourceRecord` and map the
  loaded `sprk_communication` record — **do not** widen the wrapper from inside the code-page seam.
- Non-email `sprk_communicationtype` values never mount the composer (read-only via task 040).
