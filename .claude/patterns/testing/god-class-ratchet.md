# God-Class Ratchet Pattern (server file-size forcing-function)

> **Last Reviewed**: 2026-08-15
> **Reviewed By**: code-quality-and-assurance-r3 followups (quality-godclass-refine)
> **Status**: Verified (ArchTests 38/0)

## When
Whenever you ADD a new `src/server/**/*.cs` file, or EDIT one of the frozen god-class files listed below.
This is a CI/ArchTest gate — read this BEFORE growing a large server file so it doesn't surprise you.

## Read These Files
1. `tests/Spaarke.ArchTests/GodClassGuardTests.cs` — the guard (waiver dict + rules; source of truth).
2. `projects/code-quality-and-assurance-r3/notes/red-item-analyses/RED-1..4` — the decomposition project seeds for the worst offenders.

## Constraints (what the guard enforces)
- **No NEW god-class**: a server `.cs` file not on the waiver list MUST stay **< 2,000 lines**.
- **No regression on existing god-classes**: each currently-oversized file is **frozen at its measured
  LOC** (the `Waivers` dict) and may drift only **+100 lines** (incidental-edit grace) before the gate fails.
- The 2,000 line is a pragmatic "becoming a god-class" marker, NOT a target the frozen files must hit —
  they are frozen pending their decomposition projects (RED-1/2/4). Coverage/size is a guardrail, not law.

## Key Rules (how to respond when the gate fails — do NOT just silence it)
1. **Preferred**: decompose the file (partial classes or per-concern services). When it drops below 2,000,
   **delete its waiver entry** — that ratchets the floor down permanently.
2. **If a genuine, justified addition** pushes a frozen file past its baseline+100: **re-baseline its
   waiver number** to the new LOC in `GodClassGuardTests.cs` **with a one-line reason in the PR**. This is
   a visible, reviewed decision — never a silent bump.
3. **Never** raise `NewFileCeilingLines` or add a waiver for a brand-new file without a documented reason.
4. Frozen files (2026-08-15): `SpeAdminGraphService` (4,911, RED-1), `ChatEndpoints` (4,066, RED-2),
   `ComposeService` (3,573), `ComposeDocxProjectionBuilder` (3,085), `ComposeShadowPatchEngine` (2,999),
   `DataverseServiceClientImpl` (2,864, RED-4), `DataverseWebApiService` (2,822, RED-4),
   `CommunicationService` (2,676), `ComposeEndpoints` (2,651), `PlaybookOrchestrationService` (2,528),
   `SprkChatAgentFactory` (2,380), `ComposeDocumentRenderer` (2,304), `CommunicationEnrichmentService`
   (2,087), `OfficeService` (2,038).

Regenerate the waiver numbers with:
`find src/server -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec wc -l {} \; | awk '$1>2000'`
