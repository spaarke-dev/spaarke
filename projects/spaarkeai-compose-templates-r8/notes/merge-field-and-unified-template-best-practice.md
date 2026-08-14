# Merge-Field & Unified-Template — Best-Practice Findings (r8)

> **Compiled**: 2026-08-13 (code review of the shipped R6 template + email pipeline + researcher agent on
> TipTap/Lexical/CKEditor/docxtemplater merge-field practice). Input to `/design-to-spec`.
> **Companion to**: [`dataverse-template-storage-findings.md`](dataverse-template-storage-findings.md) (storage).
> This note settles the **merge model, the token representation, the picker, and the one-vs-two entity**
> question — the parts the design doc left open (§2 open question, UC-4, UC-6).

---

## 0. TL;DR recommendations

1. **One entity — `sprk_template`.** Type optionset (Word | Email). **File column** holds the Word `.dotx`;
   **memo columns** hold the email subject/body HTML. One catalog, one picker, one merge seam (§11).
2. **One canonical token syntax — `{{token}}`.** Already the internal form on both surfaces
   (`WordTemplateService` uses it directly; `EmailTemplateService` normalizes OOB `{!field}`→`{{field}}`).
   Keep it as the single **storage/wire** syntax everywhere.
3. **Merge model = A (resolve-at-open) for v1, everywhere.** Tokens are resolved to text **server-side**
   before the editor ever sees them — exactly what the code does today. **Do NOT** put live editable
   `{{token}}` text into either editor.
4. **Live merge-field "pills" (Model B) are a deferred, Lexical-first enhancement** — NOT a Word/TipTap
   feature, because of the Compose offset-addressing-table constraint (see §3). Build it (if ever) behind a
   shared **framework-agnostic `TokenRegistry`** + a thin per-editor node.
5. **Highest-value spike**: whether the `.dotx` should carry tokens as native Word **`MERGEFIELD` field
   codes** vs literal `{{token}}` runs, for Word-client edit survivability.

---

## 1. What the shipped code already gives us (don't re-litigate)

| Concern | Reality in code | Consequence for r8 |
|---|---|---|
| Token syntax | `{{token}}` via shared `TemplateEngine` (Handlebars.NET, `NoEscape=true`, rich helpers). Email path `NormalizePlaceholders` converts `{!field}`→`{{field}}` then renders through the **same** engine. | The merge **engine** is already unified. r8 unifies catalog + picker + storage, not the engine. |
| Word merge timing | `ComposeTemplateSource` merges tokens in OOXML **before** `ComposeDocxProjectionBuilder` projects docx→TipTap HTML. | User sees finished prose. This **is** Model A. Zero TipTap token work needed for v1. |
| Email merge timing | `EmailTemplateService`/`CommunicationTemplateEndpoints` render to final HTML in the BFF. | Also Model A. |
| TipTap atom constraint | `composeNumberAtomExtension.ts` deliberately used a **view Decoration, not a doc node**, because an inline atom node desyncs the `(paraId,runIndex,offset)` offset table the save/redline path depends on (ADR-049). | A merge-field-as-editable-**node** in the Word editor is a **structural hazard**. Any Word pill must be a decoration over resolved text, or not exist. |
| Existing merge-field catalog/picker | **None.** (Grep hits are AI-playbook merge, unrelated.) | A token picker + registry is genuinely new surface → §11 justification required. |

---

## 2. The pivotal decision — Model A vs Model B

**Model A — resolve-at-open (current, recommended for v1).** Tokens merged server-side → editor gets baked
text. No placeholders visible.
- **Pro**: zero TipTap/Lexical cost, trivial DOCX round-trip, matches shipped code, no offset-table risk.
- **Con**: missing data resolves to blank/literal; user can't see or re-fill a field after open.

**Model B — live placeholder pills.** Token becomes an atomic non-editable "chip" (`‹Client Name›`) the
user can see/insert/fill; resolved at export.
- **Pro**: flexible; interactive field insertion; visible unresolved fields.
- **Con**: custom atom in **both** editors; must survive docx→HTML→docx as an addressable atom; **in the
  Word editor it collides with the offset table** (must be a decoration, which can't be *edited into*, which
  defeats "fill it in").

### Resolution (the elegant split)
- **Word / Compose → Model A, full stop.** The design's own non-goal is "Word-template authoring inside
  Compose" — makers author `.dotx` in Word. So the Word editor never needs to *insert* fields, and end-users
  get resolved prose. No pills, no node, no offset-table risk.
- **Email / Lexical → Model A for v1; Model B is the natural future here.** Lexical has **no** offset-table
  constraint, and interactive "insert a field" is most useful in ad-hoc email composition. If/when Model B
  ships, it ships **Lexical-first** as a `DecoratorNode` (true atom), reusing the shared registry.
- **`{{token}}` stays the single wire syntax** so content can move between surfaces losslessly.

This keeps v1 small and correct, and gives a clean, low-risk growth path for "flexible."

---

## 3. Token representation & serialization (for Model B, when built)

Consensus across TipTap Mention, CKEditor 5 Merge Fields, Lexical: a merge field is an **atomic,
non-editable inline unit** whose identity is in **node attributes**, never raw editable `{{}}` text.
Raw-text failure modes: caret lands mid-token (`{{cli|ent}}` corruption), spellcheck "corrections",
paste splitting, partial find/replace + formatting.

**Serialization contract (one canonical syntax, two DOM/interop forms):**
- Store the **key only** (`token`), never the human label — labels resolve from the registry at render, else
  they drift.
- Editor DOM: `<span data-token="client_name" contenteditable="false" class="merge-field">Client Name</span>`
  (`renderHTML`/`parseHTML`).
- Plaintext/export/storage: `{{client_name}}` (`renderText`).
- On load from a `{{token}}` string, a parse pass (input/paste rule or scan) rehydrates atoms, **validating
  each key against the registry**; unknown keys → a distinct "warning" pill, never silently dropped.

**Editor choices:** TipTap → thin custom `mergeField` **Node** forked from Mention (own `token` attr +
closed-set validation on insert; don't inherit `@`-mention semantics). Lexical → **`DecoratorNode`** (truly
atomic, matches the PM atom) rather than a `TextNode` token-mode (which stays "text" and inherits the
spellcheck/format edge cases).

---

## 4. DOCX interop (Model A merge fidelity — matters NOW)

Word fragments text across `<w:r>`/`<w:t>` on arbitrary boundaries, so `{{client_name}}` is frequently split
across runs — the same runs-vs-logical-text problem as annotation anchoring. `WordTemplateService` already
does split-run-aware replacement; **verify** it uses the docxtemplater-style **flatten-then-match** approach
(join a paragraph's run text → find delimiter pairs on the joined string → re-emit) and that its run-merge
**preserves non-text run children** (`<w:tab>`, breaks) — naive merge eats them.

**Rule: consolidate + merge tokens server-side, in OOXML, before HTML conversion.** Never try to
detect/reassemble split tokens in the browser after a lossy docx→HTML step — the run structure is gone.

**Spike (highest-value follow-up):** represent template tokens as native Word **`MERGEFIELD` field codes**
(`<w:fldSimple>`/`w:instrText`) vs literal `{{token}}` runs. Field codes are Word's native mail-merge form
and may survive round-tripping through the Word client better than literal braces. Decide before locking the
`.dotx` authoring convention makers will follow.

---

## 5. Token picker UX (Model B / maker guidance)

Mature reference = CKEditor 5 Merge Fields: a **closed, categorized field set** surfaced three coordinated
ways — a toolbar "Insert field" dropdown (grouped: Client, Matter, Firm, Dates; show sample values), a
menubar item, and a typed `{{` trigger opening a filtered list. Document-automation tools (Gavel/Documate,
Woodpecker) all present a **curated list, never free typing**. **Validate on insert against the registry** —
that is the anti-typo mechanism. For Spaarke: toolbar dropdown as the discoverable path, `/` or `{{`
suggestion trigger (`@tiptap/suggestion`) as the power path, both over the same registry.

For v1 (Model A) there is **no picker** — the closed token set is just the merge-context keys the server
supplies (client, matter, date, today, author). The registry (§6) is where that closed set is declared.

---

## 6. The shared `TokenRegistry` (the one new abstraction — §11-justified)

A single framework-agnostic module (shared package, e.g. `@spaarke/ui-components` or a small
`@spaarke/templates` lib) declaring the closed field set: `{ id, label, category, sampleValue, formatter? }`.
It is consumed by (a) the server merge-context builder (key names must match), (b) the future picker, (c)
validation, (d) both editor nodes if Model B ships. **Declared once** — never duplicated per editor.
- **Existing?** No token registry exists. **Extension?** Nothing to extend. **Cost-of-doing-nothing?**
  Without it the token vocabulary drifts between the Word merge context, the email merge context, and any
  picker — the exact failure the design is trying to end.

---

## 7. Entity shape — ONE `sprk_template`

Resolves the design §2 open question in favor of **one unified entity**:

| Column | Type | Word | Email |
|---|---|---|---|
| `sprk_name` | Text | ✓ | ✓ |
| `sprk_type` | Optionset (Word \| Email) | ✓ | ✓ |
| `sprk_category` | Text/optionset | ✓ | ✓ |
| `sprk_description` | Text | ✓ | ✓ |
| `sprk_payload` | **File column** (`.dotx`) | ✓ (required) | — |
| `sprk_subject` | Text | — | ✓ |
| `sprk_body` | **Memo** (HTML) | — | ✓ |
| `sprk_scope` | Optionset (Org \| Personal) | ✓ | ✓ |
| `sprk_thumbnail` (optional) | File/Image | ✓ | ✓ |

- **Why memo (not File) for email body**: editable inline in a model-driven form (no upload step),
  searchable, and it makes the **OOB `template` migration trivial** — copy `body`→`sprk_body`,
  `subject`→`sprk_subject`. (Word `.dotx` must be a File column — binary, per storage findings.)
- **Maker authoring (UC-6)**: see §7.5 — **in-context "Save as template"** is primary, not a model-driven form.
- **Email migration (UC-5)**: dual-read during transition (try `sprk_template` type=Email; fall back to OOB
  `template`), then a one-time copy migration; retire the OOB read once seeded.

---

## 7.5. Template AUTHORING — the confusing part, resolved (UC-6)

**Principle: users never see a separate "template builder." They promote something they already authored,
in the editor they already use.** A generic "create a template" form is itself a source of confusion because
the two payloads are unlike (binary `.dotx` vs HTML) — so authoring is anchored to the two existing editors.

### Primary path — "Save as template" (low-confusion, both types)

| Type | Gesture | Reuses |
|---|---|---|
| **Word** | In a Compose doc → **"Save as template"** (name + category prompt) | Compose editor + existing save path (`documents/{id}/save` already resolves OOXML bytes) → write to `sprk_template.sprk_payload` File column. **New**: a thin "save-as-template" server action. |
| **Email** | In the EmailComposer → **"Save as template"** | Lexical composer body HTML → `sprk_template.sprk_body` (+ subject). |

For **v1 (boilerplate-only, no tokens)** this is genuinely zero-confusion: write it normally, one button,
name it. No token syntax, no upload, no File-column ceremony. Pattern matches Gmail/Outlook/HubSpot
"save as template."

### Who can author (owner decision 2026-08-13)
**Any user → personal + org.** Everyone can save a **personal** template freely (`sprk_scope=Personal`,
owner-scoped). Saving a **firm/org-shared** template (`sprk_scope=Org`) is **role-gated**. The
"Save as template ▾" affordance offers "Save as my template" (always) and "Save as firm template"
(role-gated). This maps directly onto the `sprk_scope` optionset — no extra schema.

### Secondary path — "Import Word file" (power / pre-built firm templates)
Precise firm `.dotx`/`.docx` files a lawyer already built: a small **Import** button on the Templates tab
(or the model-driven form) uploads the file into a new `sprk_template` record. **Accept `.docx` too** — the
resolver already does (`TemplateFileExtensions = { ".dotx", ".docx" }`) — so users upload an ordinary Word
doc; no "Save As Template" ceremony.

### Tokens (deferred) — learned once, both editors
v1 has no tokens to insert, so authoring is write-and-save. When Model B tokens arrive, the **same**
"Insert field" toolbar dropdown (over the shared `TokenRegistry`, §6) appears in **both** editors, validated
on insert — so users never hand-type `{{client_name}}`.

### Management (trivial)
Creation is in-context; **management** (rename / recategorize / delete / toggle personal↔org scope) is a
plain list — a "manage" mode on the Quick Start Templates tab OR the model-driven grid. No custom admin app.

---

## 8. Open follow-ups (carry into spec)
1. **`MERGEFIELD` vs literal `{{token}}` runs** in the `.dotx` — spike Word-client round-trip survivability.
2. **Confirm `WordTemplateService` split-run merge preserves `<w:tab>`/non-text children** (regression risk).
3. **Model B end-state** if ever built: "template stays tokenized" vs "merge-at-export" — decide explicitly.
4. **Lexical `DecoratorNode` `importDOM`/`exportDOM`** for `<span data-token>` parity — verify vs current API
   before implementing (deferred with Model B).

## Sources
TipTap Mention + custom Node + schema; CKEditor 5 Merge Fields (strongest mature-product reference);
docxtemplater internals + MarkLogic `ooxml:runs-merge` (split-run consolidation, tab-loss caveat);
Lexical Nodes/DecoratorNode + playground `MentionNode`. Plus repo: `WordTemplateService.cs`,
`EmailTemplateService.cs`, `TemplateEngine.cs`, `ComposeTemplateSource.cs`, `ComposeDocxProjectionBuilder.cs`,
`composeNumberAtomExtension.ts`, `EmailComposer.types.ts`, `CommunicationTemplateEndpoints.cs`.
