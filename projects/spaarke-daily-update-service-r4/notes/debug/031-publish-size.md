# Debug: 031 — BFF Publish-Size Verification (Path A.5 dispatch wrapper)

> **Task**: 031-replace-handle-narrate-with-playbook-dispatch.poml
> **Date**: 2026-06-26
> **Binding**: CLAUDE.md §10 bullet 4 (publish-size per BFF-touching task)
> **Ceiling**: ≤60 MB compressed (spec NFR-01)

---

## Result

| Measurement | Value |
|---|---|
| **Compressed publish size (this task)** | **46.30 MB** (48,552,251 bytes) |
| PR 3 baseline (task 029) | 46.31 MB |
| **Delta vs PR 3 baseline** | **-0.01 MB** (effectively zero; well under +5 MB escalation threshold) |
| Cumulative vs cumulative-ceiling (55 MB) | 46.30 MB / 55 MB (8.70 MB headroom) |
| Hard ceiling (60 MB) | 46.30 MB / 60 MB (13.70 MB headroom) |

**Status**: ✅ PASS — no escalation required.

---

## Why a near-zero delta is expected

This task is a NET-NEUTRAL refactor:
- **Removed**: ~340 lines of inline LLM-prompt construction + parsing helpers
  (`BuildNarrateTldrPrompt`, `BuildChannelNarrationPrompt`, `ParseTldrResponse`,
  `ParseChannelBullets`, `BuildAllowedRegardingIdSet`, `ValidateBulletPrimaryEntityIds`,
  `GetTldrAsync`, `GetChannelNarrationAsync`, the `TldrJsonPayload` inner class)
- **Added**: ~150 lines of dispatch-wrapper code (`HandleNarrate` body +
  `ProjectPlaybookResultToNarrateResponse` + `NarrateSerializerOptions`)
- **Added**: 1 string const + 1 list element in `ConsumerTypes.cs`

Net effect on IL is slight reduction (~190 lines deleted), but since most cost in
publish-size is the closure of NuGet dependency assemblies (not endpoint LOC), the
measured delta is effectively zero.

**NO new NuGet packages introduced**. `IConsumerRoutingService` and
`IInvokePlaybookAi` were already DI-registered for 6 other consumers — this task
only added a 7th caller of the existing wiring.

---

## Verification command

```bash
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o deploy/api-publish-031/
```

Compressed via PowerShell `Compress-Archive` (matches the CLAUDE.md §10 baseline
measurement methodology).

---

## Per-task verification rule (NFR-01 / F-3)

This entry satisfies the BFF Publish-Size Per-Task Verification Rule binding
on every R4 task per spec NFR-01. Recorded for PR 4 wrap (task 036).
