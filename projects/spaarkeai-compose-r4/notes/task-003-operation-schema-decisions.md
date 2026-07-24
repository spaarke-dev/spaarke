# Task 003 — Operation-schema shape decisions (FR-11)

> **Created**: 2026-07-22 by task 003 (`003-operation-schema.poml`)
> **Artifacts**:
> - Server: `src/server/api/Sprk.Bff.Api/Services/Compose/Operations/ComposeOperation.cs`
> - Client: `src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.ts`
>   (re-exported from `src/types/compose-contracts.ts` + the package barrel `src/index.ts`)
> - Tests: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeOperationSchemaTests.cs` (server, 4 tests)
>   + `src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.test.ts` (client Jest, 9 tests)

## What was built

The shared, versioned operation contract — the spine both the client (ProseMirror step interceptor,
task 020) and the server (`ComposeShadowPatchEngine`, task 030) implement IDENTICALLY. Closed set of
exactly **ten** op types (FR-11), each anchored `{paraId, runIndex, offset|range}`, on an envelope
carrying a `schemaVersion` field (`compose-ops-v1`).

Shape borrowed from **Slate's discriminated-union op set** (`{type, anchor, properties}` — see
`notes/bridge-prior-art.md` finding #2) — the SHAPE only, not Slate's identity model: Slate's
session-only `path` is replaced by our durable key `w14:paraId` (design §0 D2, finding #4/#6).

## Anchor primitives

- `ComposeRunPoint { runIndex, offset }` — run-local point (never absolute editor position).
- `ComposeRunRange { start, end }` — intra-paragraph run-local range (start inclusive, end exclusive).
- Every op carries `paraId` (the durable `w14:paraId` coarse anchor).

## The op set (discriminator → anchor + payload)

| `type` | anchor | payload properties |
|---|---|---|
| `insertText` | paraId + `at` (RunPoint) | `text`, `marks?` |
| `deleteRange` | paraId + `range` (RunRange) | — |
| `replaceRange` | paraId + `range` | `text`, `marks?` |
| `setMark` | paraId + `range` | `mark` |
| `clearMark` | paraId + `range` | `mark` |
| `splitParagraph` | paraId + `at` (RunPoint) | `newParaId` |
| `mergeParagraph` | paraId | `targetParaId` |
| `insertParagraph` | paraId (reference) | `newParaId`, `position` (Before/After) |
| `deleteParagraph` | paraId | — |
| `setBlockAttr` | paraId | `attr` (Alignment/Style/ListOrdered/ListLevel), `value` |

## KEY DECISION — structural ops carry a SECOND paraId in the properties slot (escalation NOT fired)

The POML `<escalation><trigger>` fires only if an op type **cannot be expressed by the
`{paraId, runIndex, offset|range}` anchor alone** — e.g. needing a cross-paragraph reference the
anchor can't carry. Three structural ops (`splitParagraph`, `mergeParagraph`, `insertParagraph`) DO
reference a second paragraph:

- `splitParagraph.newParaId` — the minted paragraph that receives the trailing content
- `mergeParagraph.targetParaId` — the surviving predecessor that receives the merged content
- `insertParagraph.newParaId` — the created paragraph

**This is NOT an anchor insufficiency and does NOT fire the escalation.** Rationale:

1. The **anchor's job is to LOCATE** the op (which paragraph + which run/offset). The second paragraph
   is an **op-payload property** — exactly Slate's `properties` slot in `{type, anchor, properties}`,
   the shape the design (§0 D1, §5.6, finding #2) explicitly prescribes borrowing.
2. The second reference is itself a **`w14:paraId`** — a durable, Word-native id. It is **NOT a
   run-id** and **NOT an absolute editor position**, so it fully obeys **D2** ("never run-ids, never
   absolute positions") and **I-3** (stable addressing via `w14:paraId`).
3. FR-11 names these exact ten ops as the closed set knowing they are structural; the design already
   locked structural ops (split/merge/insert/delete paragraph) as a headline capability the R3
   paragraph-diff never had.

So the anchor model `{paraId, runIndex, offset|range}` is **sufficient** for all ten ops: it locates
each op, and structural ops additionally carry a minted/target `w14:paraId` as a property. Documented
here per POML Step 6; surfaced in the file-level doc comments of both contract files.

## Other closed-set choices (kept minimal per FR-11 "resist adding beyond the set")

- **Marks** = `Bold | Italic | Underline` — mirrors the existing `ComposeInlineRun` mark surface +
  client StarterKit (MIT). Value-carrying marks (link/color) are a deliberate future extension via the
  op properties slot, NOT part of v1.
- **Block attrs** = `Alignment | Style | ListOrdered | ListLevel` with a string `value` interpreted per
  attr — mirrors the existing `ComposeBlock` block-level fields (Kind/Level/Ordered/Alignment).
- **Envelope** = `{ schemaVersion, operations }` only. The doc-version/eTag guard (finding #5,
  LSP `didChange` version int → SPE eTag/If-Match) is a SEPARATE save-ordering concern handled by the
  save endpoint, not baked into the schema envelope (kept minimal + closed).
- **Cross-paragraph ranges** are NOT one `deleteRange` — they decompose into per-paragraph
  `deleteRange` + `mergeParagraph`, keeping every range intra-paragraph so the anchor stays durable.

## Serialization contract (cross-language, byte-mirrored)

- Server: `System.Text.Json` polymorphism via `[JsonPolymorphic(TypeDiscriminatorPropertyName="type")]`
  + `[JsonDerivedType(…, "insertText")]` — the discriminator writes/reads as the exact FR-11 literal.
- Enums serialize as their PascalCase member name (`JsonStringEnumConverter`) — same convention as the
  existing `ComposeBlockKind`/`ComposeParagraphAlignment`; the client union mirrors those literals
  (`'Bold'`, `'Alignment'`, `'After'`, …).
- Round-trip proven both directions: server test asserts a ten-op log serialize→deserialize→serialize
  is byte-stable + reconstructs every derived type + emits the exact wire discriminators; client Jest
  asserts the same log survives `JSON.stringify`→`JSON.parse` and the guards (`isComposeOperationLog`)
  accept it / reject closed-set violations.

## Verification results

- BFF build: **green** (0 errors).
- Server tests: **4/4 pass** (`ComposeOperationSchemaTests`).
- Client Jest: **9/9 pass** (`compose-operations.test.ts`).
- Tier-1 NetArchTest `ADR013_ComposeFacadeTests`: **green** — `Services/Compose/Operations/` (nested
  under `Services/Compose`) references no `IOpenAiClient` / Nodes executor / `IConsumerRoutingService`.
- ADR-007: no `Microsoft.Graph` type in the contract.
- Publish size: **46.11 MB compressed** (incl PDBs) — ≤60 MB ceiling; within the ~49.63 MB baseline
  band; zero new runtime package (no `.csproj` change).
- CVE: no NEW HIGH CVE from this task (zero package refs added). The pre-existing transitive
  `System.Security.Cryptography.Xml 8.0.3` HIGH advisories are baseline debt, unrelated to task 003.

## Placement Justification (root §10)

The schema stays in `Services/Compose/Operations/` — a pure C# data contract (records + enums,
`System.Text.Json` only) that is the byte-author INPUT for the future `ComposeShadowPatchEngine`
(Compose-domain save/patch orchestration, transactionally coupled to the Compose save lifecycle).
It is NOT AI, NOT a separate deployable, references no AI-internal type (ADR-013 Tier-1) and no Graph
type (ADR-007), and adds zero NuGet (publish-neutral). Per `.claude/constraints/bff-extensions.md` it
belongs in-process in the BFF alongside the existing `Services/Compose/*`.
