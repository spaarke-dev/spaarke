# Design — Spaarke Word-Native AI & The Open Platform — r1

> **Status**: DRAFT v3 — 2026-09-04 (v3 splits the add-in productivity surface out to `spaarkeai-word-add-in-r1`)
> **Method**: Fable-model investigation — 6 parallel research agents (competitive landscape · Word extensibility platform · MCP ecosystem · Compose engine · integration surfaces · AI capability layer), primary sources only.
> **Sibling project**: [`spaarkeai-word-add-in-r1`](../spaarkeai-word-add-in-r1/design.md) — the Office add-in as a Spaarke productivity surface (Save · Compose · Find). **That project is a prerequisite for this one.**
> **Next step**: owner review → `/design-to-spec`

### Locked decisions (owner)

| # | Decision |
|---|---|
| L-1 | Segment: **both, in-house corporate legal first** |
| L-2 | Compose relationship: **Compose is the engine, Word is a new surface** |
| L-3 | Horizon: **phased with an MVP; full-platform vision visible** |
| L-4 | **Compose and Word are compatible, not seamless.** No continuous round-trip. One document, one editor at a time. |
| L-5 | **MVP = the open platform.** Spaarke-native AI capability grows over subsequent releases. |
| L-6 | **No iManage / NetDocuments integration in r1.** |
| L-7 | **The add-in *surface* is out of scope** — pane shell, tabs, save flow, document identity all belong to `spaarkeai-word-add-in-r1`. This project consumes them. |

---

## 0. Product strategy

> **We are the system of record** — matter metadata, users, tasks, and documents (SPE as DMS).
>
> **For work-product analysis and drafting, users work in Word and choose whatever legal AI tool they want** — Harvey, Legora, Microsoft Copilot, Claude, ChatGPT — **and we also offer a Spaarke-native option.**
>
> **If a user chooses a third-party tool, that tool can read our Dataverse records and SPE documents, and — importantly — its work product saves easily back into Spaarke Dataverse and SPE.**

This project owns the **open platform** half of that statement, plus the long-term **Spaarke-native AI depth** in Word. The practical add-in surface — the pane, the tabs, the save flow — is `spaarkeai-word-add-in-r1`.

The strategic bet: we do not win by having the best redliner. **We win by being the platform the user never leaves, whichever AI they pick.**

---

## 1. Why now — the market facts that bind this design

| Fact | Consequence |
|---|---|
| **Microsoft shipped a Legal Agent in Word** (30 Apr 2026; $30/user/mo; Claude-backed; playbook review + tracked-change redlines) and acqui-hired Robin AI's team into the Word org | Baseline in-Word redlining is a commodity platform feature. Do not build a business on it. This is why L-5 is correct. |
| **iManage** and **NetDocuments** both ship MCP servers positioned as "the governed substrate beneath AI tools" | The data-layer play is validated *and* contested — for **documents**. Neither exposes matter/operations context. That gap is ours. |
| **Harvey** consumes third-party MCP servers via an admin-gated Connector Library; **Legora** consumes "any MCP server" incl. customer-built | The open platform has real consumers among the vendors that matter most |
| **M365 Copilot declarative agents with MCP** GA (2025-12-15); no per-user Copilot licence required (Copilot Chat + PAYG credits) | The cheapest Word presence available, and the only *proven* third-party in-Word channel |
| **MCP spec 2026-07-28**: stateless, sessions removed, Streamable HTTP only, DCR deprecated | Build stateless from day one — satisfies both current and new client semantics |
| **No vendor's Word add-in surface verifiably consumes MCP** | Third-party MCP consumption happens in those tools' *web assistant* surfaces. Do not promise "Harvey's Word pane will read your matters." |

**Requirements Harvey imposes on any connector** (and therefore on us): HTTPS, OAuth 2.1 + PKCE (S256) **required**, RFC 8414 AS metadata **required**, RFC 9728 Protected Resource Metadata **required**, DCR optional. We would build all of these anyway.

---

## 2. Scope

### 2.1 Goals

- **G1 — The round trip.** External AI tools read Spaarke matter context and documents, and save work product back, under per-user authorization.
- **G2 — A Word presence for non-MCP users.** ⚠️ **The agent already exists and is deployed** — see §2.4. G2 is therefore **not "build a declarative agent"**; it is **fix its authorization and decide its action runtime**.
- **G3 — Playbooks as governed data** exposed over MCP (see §7 for the honest sizing).
- **G4 — Spaarke-native Word AI depth** — tracked-change authoring, agreement review in-document. *Later releases.*

### 2.2 Non-goals

- **NOT** the add-in surface (L-7) — pane, tabs, save, document identity are `spaarkeai-word-add-in-r1`.
- **NOT** competing with Microsoft's Legal Agent on generic playbook redlining.
- **NOT** building a legal research corpus (Westlaw / Practical Law / Shepard's equivalents).
- **NOT** replacing the Compose React editor (L-2).
- **NOT** seamless Compose↔Word round-tripping (L-4). No continuous sync, no divergent-body merge.
- **NOT** integrating iManage or NetDocuments (L-6). See §3.3 for the federation alternative.
- **NOT** shipping to external/CIAM users (ADR-028 A1/E-3). Workforce-only absent an amendment.
- **NOT** waiting for Spellbook / Definely to add MCP client support.
- **NOT** building on the Excel-only "Copilot skills" preview. Monitor for Word parity; do not architect on it.
- **NOT** unfreezing the node-based playbook engine (`sprk_playbooknode`). New capability uses the linear ADR-043 Action spine.
- **NOT** pointing customers' tools at Microsoft's first-party Dataverse MCP (`/api/mcp`) — generic tool shape, no Spaarke semantics, bills customers Copilot credits per call, cannot reach SPE.
- **NOT** ripping out Spaarke's AI Search retrieval for a preview Microsoft API (§3.4).

### 2.4 The declarative agent already exists (G2 rescoped)

Owner evidence (2026-09-04): a **"Spaarke AI" agent is live in Word's Copilot pane** — "Created by Spaarke ✓", chat-only mode, Copilot's default document prompts — sitting alongside Claude and Harvey for Word in the same ribbon.

It is ours, and it is a full declarative agent **plus an OpenAPI API plugin**, at [`src/solutions/CopilotAgent/`](src/solutions/CopilotAgent/):

| Artifact | Role |
|---|---|
| `declarativeAgent.json` | The agent — name, instructions, capabilities |
| `spaarke-api-plugin.json` | **API plugin**, `auth.type: OAuthPluginVault` — the action runtime |
| `spaarke-bff-openapi.yaml` | The BFF surface the plugin calls |
| `appPackage/manifest.json` + `scripts/Deploy-CopilotAgent.ps1` | Packaging + deploy |

**State**, from `ai-m365-copilot-integration`:
- ✅ **Inbound auth works end to end** — consent card → sign-in → Bearer token with `scp=access_as_user`, `aud=api://1e40baad-…` → BFF accepts via multi-audience `PostConfigure<JwtBearerOptions>`. OAuth is registered in Teams Dev Portal (Entra SSO did not work — **do not change this**).
- 🔴 **Downstream is not user-scoped.** The endpoints the plugin calls use app-only Dataverse auth with no OBO. `GET /api/v1/events` is documented as *"Returns ALL events or none — not scoped to current user."*

**This is the same defect shape as UAC r2's 442-document exposure**: the agent works, but results are not permission-trimmed. Security gap, not a functionality gap — it should not be demoed broadly until D-4 lands.

**What G2 becomes**:
1. **Fix the OBO second hop** — which is already D-4, this project's first task. It fixes the agent *and* the MCP server, because it is the same missing exchange.
2. **Decide the action runtime.** The agent uses an **OpenAPI API plugin** today; this design assumed **MCP actions**. Those are two runtimes for the same agent.

**Recommendation on (2)**: fix OBO first (needed either way, and it is a security fix), then **converge the agent onto `Sprk.Mcp`** so there is one tool surface for every consumer — external tools and our own agent alike. Do not throw the API plugin away on day one; run it until MCP reaches parity, then retire it. Maintaining two permanent action runtimes over the same capabilities would violate CLAUDE.md §11.

### 2.3 The MVP in one paragraph

A lawyer drafts in Word with **whatever AI they chose**. Their tool pulls matter context from Spaarke over MCP — or, if it can't speak MCP, the Spaarke declarative agent in Word's Copilot pane does. When they're done, output saves back to Spaarke: the tool pushes it over MCP, or the user clicks the Spaarke ribbon button (owned by the sibling project). **Nothing leaves the platform.**

---

## 3. Design constraints we have chosen

### 3.1 Compatible, not seamless (L-4)

ADR-049 invariant 4 records the mechanical reason: **Word regenerates `w14:paraId` on save.** Cross-save identity is structurally unavailable. Project history confirms chasing it is a trap — R4's surgical `(paraId, runIndex, offset)` patching became the HTTP 422 treadmill; R6's whole-body rebuild caused silent fidelity loss. Both superseded.

**The model**: one document, one editor at a time. Handoff is at the **document** level, not the edit level.

**What this buys**: when native editing arrives (G4), the anchor problem collapses to *re-snapshot per interaction* — capture, project, anchor, apply within one user gesture, where paraIds are stable. Ordinary engineering, not research.

**What this costs, deliberately**: a Compose chat session cannot follow the user into Word mid-draft. Session context is per-surface. Accepted.

**Safety net (all shipped)**: `If-Match` with a single rebase retry; a new immutable SPE version per save; webhook-driven external-change detection (`SpeSyncOrchestrator`); annotation re-anchoring on reload (`AnnotationReanchorService`). Posture stays last-writer-wins with a warning (ADR-049 invariant 5).

### 3.2 Two save-back paths

MCP write-back is elegant but only works for tools that support it. **The Spaarke ribbon button works for every tool, because it operates on the document, not on the AI tool** — Harvey, Spellbook, Definely, Copilot all produce output *in the document*.

The platform supports this: multiple third-party add-ins run side by side with isolated runtimes, separate ribbon groups, and simultaneously docked panes.

**Division of labour**: the ribbon path is built by `spaarkeai-word-add-in-r1`. This project builds the **MCP-initiated** path.

### 3.3 No DMS integration — federate instead

For in-house corporate legal (L-1 first segment), customers largely don't run iManage. SPE-as-DMS is the point.

For firms there's a tension worth naming: **you cannot be the system of record in a firm that runs iManage.** Their DMS owns documents. Spaarke's honest role in a firm is the matter/operations layer *alongside* their DMS — a different pitch, not the same pitch with a connector.

**The out**: iManage and NetDocuments both ship MCP servers. The customer's AI tool consumes **ours and theirs**. We don't integrate; the tool federates. Costs us nothing and fits §0 exactly. Build a direct connector only if a named deal forces it, and keep it to "resolve a link into matter context" + "save output into their DMS" — reference and hand off, never sync.

### 3.4 Microsoft substrate boundary

**Principle: delegate undifferentiated substrate; keep what encodes domain judgment.**

| Layer | Decision |
|---|---|
| Copilot as a **surface** (declarative agents) | **Lean in.** Cheapest Word presence; only proven third-party in-Word channel; no per-user Copilot licence needed. |
| Identity / auth (Entra, NAA, OBO) | **Already all-in.** Correct. |
| **Retrieval over SPE** | **Keep Spaarke's AI Search index as primary**, but design the MCP `search` tool with a **swappable backend** and pilot the Copilot Retrieval API `sharePointEmbedded` source behind it. That source is preview, needs ≥1 Copilot licence in-tenant to initialize the semantic index, and meters on the customer. |
| Capability layer (Actions/Bindings, Compose engines, matter model, access evaluator) | **Spaarke-native.** Never ship two competing implementations of "review this NDA." |

**Licence-gating caution**: Agent 365 / Work IQ paths are M365 Copilot licence-gated. Spaarke's own MCP path works for **100% of users**. Spaarke's path is the floor; Microsoft's is the upgrade.

*(Open: a focused research pass on the current Work IQ / Foundry retrieval-and-grounding story before finalizing this boundary — §13 Q4.)*

---

## 4. Prior art in our codebase

> **Add-in archaeology now lives in the sibling project.** For the pane, manifests, adapters, auth wiring, save flow, and document identity, read [`spaarkeai-word-add-in-r1/design.md`](../spaarkeai-word-add-in-r1/design.md) §2 and its [`ADDIN-CONTEXT-FROM-EMAIL-R2.md`](../spaarkeai-word-add-in-r1/ADDIN-CONTEXT-FROM-EMAIL-R2.md). This section covers only what *this* project builds on.

### 4.1 Auth — the spine an MCP server reuses

```
Client → Entra token (audience = the MCP server's App ID URI)
  → validate audience → OBO via MI-as-FIC client assertion (SECRET-FREE, ADR-028 A4)
  → Graph / SPE / Dataverse as the USER
```

`.WithClientSecret` is arch-test-enforced as a violation ([CredentialGuardTests.cs](tests/Spaarke.ArchTests/CredentialGuardTests.cs)). The OBO chain is proven in production by the add-ins.

### 4.2 Compose engines — what we have for G4

`DocumentFormat.OpenXml` 3.5.1, pure SDK DOM manipulation. `WmlComparer` was removed after a gate proved it strips `w14:paraId` and drops tables on real firm templates.

**Tracked changes.** Spaarke **already authors native `w:ins`/`w:del`**, two live authors: `ComposeDocumentRenderer` (which authors *new* revisions attributed to the saving user — user-edit revisions "arrive author-less by design") and `ComposeShadowPatchEngine` (`(paraId, runIndex, offset)` surgical patching). It also **reads** existing revisions three ways.

**Native Word comments** are authored on save — `EnsureCommentsPart` plus append-only addition to the carrier's comments part with re-authored `w:commentRangeStart/End`. What was retired is `DocxAnnotationWriter`, the *text-search-anchored push shuttle*.

**Crown jewels** (all `byte[]`-in/out, stateless, DI-singleton, no Graph/AI types — editor-agnostic, NetArchTest-enforced): `NumberingComputationEngine` (24/24 golden-label Word parity on a real firm corpus), `CitationResolver`, `ComposeDocumentRenderer` + `ComposeBlockMerge` (R8: untouched-block preservation **18.08% → 100%**, zero drift over 5 round trips).

**The anchoring contract is editor-neutral**: `ProposedEdit { new_text, rationale, sources, target_para_id, target_ref }`. **Anchor-only placement; no text-search fallback** (ADR-049 I-7), six deterministic refusal kinds. *Honesty*: `ComposeEditAnchorPass` (the server validator) was retired in task 064 and has **no production caller** — placement happens client-side in `usePendingRedline.ts`.

**Revivable dead code**: `CriticMarkupRenderer` and `SemanticAppendixGenerator` are DI-registered with zero callers. The former renders existing revisions/comments as CriticMarkup for the LLM — exactly what Harvey does. Cheap, already tested.

### 4.3 The AI catalog is already shaped for this

- **`office` is a declared `sprk_surfaces` token** ([Binding.cs:88](src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs#L88)) with **zero live rows**.
- `GET /api/ai/capabilities?surface=office` exists; dispatch is `POST /api/ai/chat/sessions/{id}/dispatch` with a Binding GUID → SSE, zero LLM in routing (ADR-039).
- **The closed catalog is an MCP manifest in all but name**: ~39 `sprk_analysistool` rows with tool code, LLM-facing description, JSON schema, and `sprk_sideeffectclass`.

### 4.4 What does not exist

**No MCP server** — zero matches for `ModelContextProtocol`/`McpServer` in `src/`; the `mcp-tool-handler` skill is aspirational, `.mcp.json` is developer tooling. **No fragment-level edit application** — server "apply edit" re-authors whole package bytes, impossible while Word holds the file open. **No tenant-editable playbooks.** **No clause library.**

### 4.5 The precedent that must not repeat

The M365 Copilot agent gateway ([Api/Agent/](src/server/api/Sprk.Bff.Api/Api/Agent/), 23/31 tasks) is **built and blocked**. Per [current-task.md:15](projects/ai-m365-copilot-integration/current-task.md#L15): inbound token acceptance was **solved**; the blocker is the **second hop** — no OBO, so the BFF cannot act as the user downstream.

Compounded by merged UAC r2: **BFF reads are app-only, so Dataverse row security is inert on that path.** Demonstrated defect — a user denied Read on all 442 documents of a matter still saw and downloaded them via `POST /api/ai/search`.

**Rule for this project: OBO is the first task, not the last.**

---

## 5. Word platform constraints (binding for G4)

Floor: **WordApi 1.6** (GA, Windows 2308 / Aug 2023).

**Hard constraints**:
1. **No API authors a tracked change with an arbitrary author.** Author is always the signed-in user. Every incumbent accepts this; so do we.
2. **`insertOoxml` silently drops `numbering.xml` changes** ([office-js#5243](https://github.com/OfficeDev/office-js/issues/5243)). Never insert numbered clauses as fragments referencing new numbering definitions.
3. **No pagination APIs** — page/line numbers unreadable. Compose already defers this honestly (WS-5).
4. **`Document.compare` is desktop-only** (WordApiDesktop 1.1/1.2). Word-web needs a server diff.
5. **5-second UI-block auto-restart** — all model calls server-side. Already our architecture.
6. **SPE content is invisible to M365 Copilot** until a tenant-governed container-type discoverability setting is enabled.
7. **No third-party extensibility of native Agent Mode.**

### 5.1 🔬 The spike that decides G4

> **Does `Word.Range.insertOoxml` preserve `w:ins`/`w:del` revision marks and author attribution?**

- **YES** → the server renders pre-redlined OOXML fragments (reusing `ComposeRunAuthor`/`ComposeShadowPatchEngine`, which already author exactly these elements) and the add-in inserts them. Full engine reuse.
- **NO** → the add-in toggles `changeTrackingMode = TrackAll` and applies plain edits; Word attributes them to the signed-in user. Engine reuse narrows to text-and-anchor.

Because of L-5 this does **not** block the MVP — run it before committing G4 scope.

---

## 6. Architecture

```
┌────────────────────────────────────────┬──────────────────────────────┐
│ THE OPEN PLATFORM  (this project, MVP) │ THE ADD-IN SURFACE           │
│                                        │ (spaarkeai-word-add-in-r1)   │
│ Harvey · Legora · Claude · ChatGPT      │  ┌────────────────────────┐  │
│         (their assistant surfaces)     │  │ Spaarke Office add-in  │  │
│                    │                   │  │  · document identity   │  │
│ M365 Copilot declarative agent ────────┼─▶│  · ribbon save-back    │  │
│         (the in-Word channel)          │  │  · Save│Compose│Find   │  │
│                    │                   │  └───────────┬────────────┘  │
│                    ▼                   │              │               │
│        ┌──────────────────────┐        │              │               │
│        │  Sprk.Mcp  (NEW)     │        │              │  G4: native   │
│        │  separate deployable │        │              │  AI depth     │
│        │  stateless · Entra AS│        │              │  (later)      │
│        │  PRM · audience · OBO│        │              │               │
│        └──────────┬───────────┘        │              │               │
└───────────────────┼────────────────────┴──────────────┼───────────────┘
                    ▼                                   ▼
     ┌────────────────────────────────────────────────────────────┐
     │           THE SHARED SPINE  (mostly exists)                │
     │  Capability catalog — Actions + Bindings (ADR-039)         │
     │  Compose engines — numbering · citations · renderer ·       │
     │                    patch engine · annotation reader        │
     │  Unified access evaluator (UAC r2) — per-user OBO          │
     │  Retrieval — AI Search (swappable backend, §3.4)           │
     │  SPE (SpeFileStore) + Dataverse (matter · users · tasks)   │
     └────────────────────────────────────────────────────────────┘
```

### 6.1 The MCP layer

**D-1. `Sprk.Mcp` is a separate deployable.** ADR-013 names this exact case; all four criteria hold (§10). Reinforced by the BFF's 60 MB ceiling (baseline ~44.96 MB). It shares `Spaarke.Core`/`Spaarke.Dataverse` and calls the BFF's existing surface rather than growing it.

**D-2. Stateless, Streamable HTTP only** (spec 2026-07-28). No protocol sessions, no `initialize`, no SSE resumability. Cross-call state is a server-minted handle keyed `<user_id>:<handle>` — **possession of a handle is never authentication**. Satisfies both current-client (2025-06-18) and new semantics; no sticky routing.

**D-3. Entra as authorization server; pre-registration per consumer.** Entra supports **neither DCR nor CIMD** — the practical crux.
- MCP server = an Entra app whose App ID URI is the canonical server URL (also the RFC 8707 resource identifier → tenant isolation at token level).
- **3–4 narrow delegated scopes**: `records.read`, `documents.read`, `documents.write`, `output.save`. No omnibus scope.
- Serve **PRM (RFC 9728)**; 401 + `WWW-Authenticate` with `resource_metadata`; 403 + `insufficient_scope` for step-up.
- **Validate token audience** against the canonical URI. **Never accept or transit a foreign-audience token** — spec-forbidden, and the structural defence against confused deputy.
- Pre-authorize known client IDs; issue pre-registered credentials to Claude orgs (which accept manual client ID/secret), Harvey (at library submission), Legora.

**D-4. Per-user OBO, always. Never app-only.** Non-negotiable per §4.5. Audience-validate, then OBO to Dataverse (impersonation → row-level security) and Graph (SPE). The UAC evaluator runs on every read.

**D-5. ~8–10 task-shaped tools, namespaced `spaarke_*`.** Deterministic `tools/list` ordering; `outputSchema` on everything.
- `search` + `fetch` — **exact OpenAI-compatible schema** (mandatory for ChatGPT's GA connector path, harmless elsewhere)
- `get_matter_context` — one call returning the assembled matter brief. **The differentiating tool**: matters, parties, deadlines, tasks, and — uniquely — *who is on this matter and what can they see*. No DMS MCP server can answer that.
- `list_documents`, `get_document_summary`, `get_playbook` (§7)
- `save_document`, `save_note` — writes (G1)

**D-6. Never ship full document text from `search`.** Return summaries + metadata + stable IDs. `fetch` supports `mode: summary|full|section`. Cap result size server-side.

**D-7. Write tools are self-protecting.** Harvey may not prompt users before connector write actions. So: idempotency keys, explicit-confirmation arguments for destructive ops, `readOnlyHint`/`destructiveHint` annotations, and audit logging attributing **user + client app + tool call**. Do not depend on client-side consent.

**D-8. Tool descriptions are a prompt surface** — read by external models and reviewed at directory submission. Keep them instruction-free and free of tenant data.

**D-9. Multi-tenancy rides the token.** Per-stamp servers for Model 2; for shared Model 1, scoping rides the token's `tid` + Dataverse impersonation — **never a client-supplied tenant argument**.

**D-10. The declarative agent is the in-Word channel** (G2). Packaged with the add-in's unified manifest — coordinate with the sibling project, which owns the Word manifest migration.

### 6.2 Native AI depth (G4 — later releases)

**D-11. Reuse the anchoring contract; build a Word materializer.** `ProposedEdit` with `target_para_id`/`target_ref` stays. `usePendingRedline.ts` does not port — Word IS the editor.

**D-12. Anchor by re-snapshot per interaction** (per L-4 / §3.1).

**D-13. Revive `CriticMarkupRenderer`** so the AI sees pre-existing tracked changes.

**D-14. Never insert numbered clauses as OOXML fragments** referencing new numbering definitions.

**D-15. Promote `ComposeShadowPatchEngine` to a first-class Word engine?** Its `(paraId, runIndex, offset)` surgical patching authoring native `w:ins`/`w:del`/`w:comment` is precisely the shape Word needs. Currently marked transitional/retirement-tracked; its gate evidence forbids deletion but did not anticipate a second consumer. **Owner decision (§13 Q2).**

---

## 7. Playbooks — honest scope

The competitive white space is real: **nobody offers a cross-vendor playbook store.** A firm running Legora + Definely + Copilot maintains three copies of its positions.

But the claim must be sized honestly. **Every vendor has its own playbook object and its own authoring** — Spellbook Custom Playbooks, Legora rule-based playbooks, Harvey's in-Word builder, Microsoft's Legal Agent taking "an internal playbook" with undocumented plumbing. **Not one documents an import API or an MCP playbook contract.**

| Approach | Verdict |
|---|---|
| **Retrieval-shaped** — MCP `get_playbook(...)` returns content; the tool pulls it into context as *grounding* | ✅ **Adopt.** Works now, no vendor cooperation. But it lands as text, not as the tool's native playbook engine. |
| **Export-shaped** — generate artifacts in each vendor's import format | ❌ N brittle integrations. Skip. |
| **Spaarke-applied** — our own `compare-to-playbook` runs it; tools consume the *result* | ✅ **Already ships.** Fully under our control. |

**Scope: small.** A playbook is agreement type + positions, each `{issue, preferred, acceptable fallback, unacceptable, rationale, optional model clause}`. No rules engine. And we are closer than it appears — the KNW-001..012 packs in `spaarke-rag-references` already *are* this (NDA is the deep exemplar). The entity is "make those tenant-editable and per-matter."

**The unknown is answerable only by asking.** Harvey's Connector Library is a form-gated partner process — a conversation channel. Legora's custom-MCP support is documented but surface-silent. **Action: ask both directly before this anchors the roadmap** (§13 Q3).

**Sequencing: Release 2.** In r1, expose `get_playbook` over the existing system corpus so the contract exists; build the tenant-editable entity once the vendor conversations land.

---

## 8. Components

| Component | Layer | Purpose | Release |
|---|---|---|---|
| `Sprk.Mcp` | New deployable | Stateless MCP host, OAuth 2.1 resource server, PRM, tool surface | **MVP** |
| Spaarke declarative agent | Manifest (with sibling project) | Copilot-pane presence via MCP actions (G2) | **MVP** |
| `wordRedlineMaterializer.ts` | Add-in | Anchor → Word `Range` → tracked-change application | R2 |
| `WordTrackedChangeReader` | Add-in | Read revisions → feed `CriticMarkupRenderer` | R2 |
| `ComposeFragmentRenderEndpoint` | BFF `/api/compose` | `ProposedEdit` + context → OOXML fragment (spike-dependent) | R2 |
| Tenant playbook entity + authoring | Dataverse + UI | §7 | R2/R3 |
| `DocumentCompareService` | BFF `/api/compose` | Server diff fallback for Word-web | R3 |

**Revivals** (already written, DI-registered, zero callers): `CriticMarkupRenderer`, `SemanticAppendixGenerator`, `ComposeEditAnchorPass`.

**Compose reuse classification**

| Class | Components |
|---|---|
| **REUSE-AS-IS** | `ComposeContentModelProjector` · `ComposeDocxProjectionBuilder` · `NumberingComputationEngine` · `CitationResolver` · `ComposeReferenceMapping` · `ComposeDocumentRenderer` (+`BlockMerge`/`RunAuthor`/`NumberingAuthor`/`StyleCatalog`/`OoxmlPrimitives`) · `ComposeShadowPatchEngine` · `DocxAnnotationReader` · `AnnotationReanchorService` · `ComposeAnchorResolver` · `ParaIdPreParser` · `ComposeBaselineParaIdStamper` · `ComposeTextFold` · `ComposeTemplatePartMergeEngine` |
| **REUSE-WITH-ADAPTATION** | `ComposeEditModels`/`ComposeEditAnchorPass` (re-expose) · `POST /api/compose/project` (full-package input) |
| **COMPOSE-EDITOR-ONLY** | HTML projection output · TipTap extensions · `docxBridge` · `ComposeEditor`/`Workspace`/toolbars · `usePendingRedline` · `TrackChangesExtension` · `ComposeConflictDialog` · `OffsetAddressingTable` semantics |
| **MUST-BUILD-NEW** | Re-snapshot anchor flow · in-Word tracked-change application · fragment-render endpoint |

---

## 9. Phasing

**Sequencing rule**: the OBO second hop (D-4) blocks everything. It is the first task.
**Dependency**: G2's declarative agent rides the sibling project's unified-manifest migration.

**Phase 0 — De-risk**: owner decisions §13; Harvey Connector Library BD motion (form-gated — **start now**, it gates Phase 2).

**Phase 1 — MVP: the open platform.** `Sprk.Mcp` — stateless, Entra AS, PRM, audience validation, **OBO first** · `search`/`fetch`/`get_matter_context`/`list_documents`/`save_document` · Claude verified as first client · declarative agent (G2).
*Gate*: an external client retrieves matter context under that user's permissions, with a passing **negative** test; a foreign-audience token is rejected; a document written back lands as a proper `sprk_document`.

**Phase 2 — Interop breadth.** Harvey Connector Library submission · Legora verification · ChatGPT connector · `get_playbook` over the system corpus.

**Phase 3 — Native depth (G4).** 🔬 `insertOoxml` spike (§5.1) · clause rewrite as tracked changes · TC-awareness · agreement review with native Word comments · tenant playbook entity.

**Phase 4 — Platform.** Document-wide agentic run · document-vs-document compare · firm-segment federation (§3.3) · per-stamp MCP deployment for Model 2.

---

## 10. Placement Justification (CLAUDE.md §10)

| Addition | Placement | Rationale |
|---|---|---|
| MCP host + OAuth 2.1 resource server | **Separate deployable `Sprk.Mcp`** | ADR-013 names this case. All four criteria hold: no latency coupling with BFF synthesis (external agent loops are asynchronous); no transactional coupling with session/safety state (stateless per D-2); bounded surface (8–10 tools); no duplication of latency-sensitive components. Reinforced by the 60 MB ceiling (baseline ~44.96 MB). |
| Fragment-render / compare / anchor-pass | **In BFF**, extending `/api/compose` | Reuses the Compose engine directly; extracting would duplicate it. |
| Tenant playbook entity | **Dataverse + BFF CRUD** | Follows existing catalog-entity patterns. |

Publish-size measured per §10 bullet 4 on every BFF-touching task.

---

## 11. Component Justification (CLAUDE.md §11)

| New component | Overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `Sprk.Mcp` | Copilot agent gateway (`Api/Agent/`) | Different protocol, auth model, deployable per ADR-013 + size ceiling | No external tool reaches Spaarke — the MVP does not exist |
| Spaarke declarative agent | The Word add-in | Different surface, different licensing, and the only *proven* third-party in-Word channel | We forfeit the cheapest Word presence available |
| `wordRedlineMaterializer.ts` | `usePendingRedline.ts` | 104 KB of TipTap/ProseMirror-specific code; Word IS the editor | Native edits cannot land in Word (R2+) |
| Tenant playbook entity | `spaarke-rag-references` KNW packs | System-wide markdown; not tenant-editable, not per-matter | "Compare to playbook" can only use Spaarke's corpus, never the customer's positions |

---

## 12. ADR Tensions (CLAUDE.md §6.5) · Security

| ADR | Tension | Path |
|---|---|---|
| **ADR-028** (Auth) | External AI tools authenticating to an MCP server is a new plane; ADR-028 has no OBO exception for external planes | **B — amendment.** Define the MCP client plane: pre-registered Entra clients, audience-validated tokens, mandatory OBO, no token passthrough. |
| **ADR-013** | Does `Sprk.Mcp` qualify as a separate deployable? | **C — comply.** ADR-013 names the case and lists criteria; we satisfy all four. Document the check in the PR. |
| **ADR-049** | The invariant pair assumes Compose authors the bytes; when Word is the editor, **Word owns the bytes** | **A — project-scoped exception.** ADR-049's save contract governs the Compose-editor path only. Document explicitly. |
| **ADR-049 invariant 4** | The Word anchor flow needs identity across a gesture | **C — comply.** The invariant is why L-4 is correct. Re-snapshot; never key on paraId across saves. |
| **ADR-039** | MCP exposes tools to an external agent loop we don't control | **C — comply.** The closed catalog *is* the safety mechanism. Expose only catalog-declared tools; keep the side-effect gate server-side (D-7). |

**Security invariants**: per-user OBO everywhere (D-4) · UAC r2 evaluator on every read · audience validation as the structural confused-deputy defence · state handles are not authentication · scope minimization · fail-closed search trimming · tenant scoping rides the token · write safety is ours, not the client's · tool descriptions instruction-free · `sprk_issecure` suppresses derived and org-membership access · workforce-only (ADR-028 A1/E-3).

---

## 13. Risks & open questions

| Risk | Severity | Mitigation |
|---|---|---|
| MCP auth second hop repeats the Copilot gateway failure | **HIGH** | D-4 non-negotiable; OBO is the first task |
| The playbook differentiation claim is weaker than it sounds (§7) | **HIGH** | Sized to retrieval-shaped; vendor conversations before it anchors the roadmap; R2 not MVP |
| Microsoft's Legal Agent commoditizes native Word capabilities | MEDIUM | L-5: we do not compete there |
| No vendor Word add-in consumes MCP | MEDIUM | Value lands in vendors' assistant surfaces; the DA is the in-Word channel. **Do not oversell.** |
| Harvey Connector Library is form-gated and reviewed | MEDIUM | Start the BD motion in Phase 0 |
| Entra lacks DCR/CIMD | MEDIUM | Pre-registration covers every strategic client. Avoid an OAuth proxy — it imports the full confused-deputy burden. |
| `insertOoxml` does not preserve revisions | MEDIUM | Fallback to `TrackAll` + plain edits; Phase 3 concern only |
| Metering traps (Copilot credits, Retrieval API PAYG) | MEDIUM | Model into stamp pricing; keep AI Search primary (§3.4) |

**Open questions**
1. **MCP server shape** — one server with two tool families (Dataverse + SPE), or two? *Recommend one: `get_matter_context` spans both.*
2. **Promote `ComposeShadowPatchEngine`** to a first-class Word engine (D-15)? **Owner call.**
3. **Playbook vendor conversations** — who owns reaching out to Harvey and Legora, and when? (§7 blocks on this.)
4. **Work IQ / Foundry grounding** — a focused research pass is needed before finalizing the §3.4 delegation boundary.
5. **Declarative agent ownership** — this project or the sibling (which owns the Word manifest)? Recommend: manifest by the sibling, agent definition + MCP wiring here.

---

## 14. Acceptance (draft — closed set to be finalized in spec)

1. An external MCP client (Claude, and/or a Copilot declarative agent) authenticates, lists Spaarke tools, and retrieves matter context **scoped to that user's permissions**.
2. **Negative case**: a user denied access to a matter cannot retrieve it through any MCP tool.
3. A **foreign-audience token is rejected** by the MCP server.
4. An external tool writes a document back into Spaarke; it lands as a proper `sprk_document` with container resolution and matter association.
5. The Spaarke declarative agent appears in Word's Copilot pane and answers a matter-context question.
6. `get_playbook` returns firm positions to an external client.
7. Publish-size delta measured and within ceiling on every BFF-touching change.

---

<hot-path-declaration>
  <bff>Y</bff>
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
