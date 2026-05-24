# Project Reference Graph (Task 013)

> **Source**: `dotnet list reference` recursive
> **Captured**: 2026-05-24
> **Note per UQ-06**: `Spaarke.Core` + `Spaarke.Dataverse` are inventory-only in this project; no edits

---

## Recursive reference tree

```
Sprk.Bff.Api (src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj)
├── Spaarke.Core (src/server/shared/Spaarke.Core/Spaarke.Core.csproj)
│   └── Spaarke.Dataverse (src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj)
│       └── (leaf — no project references)
└── Spaarke.Dataverse (src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj)
    └── (already shown above)
```

---

## Direct + transitive project closure

| Project | Path | Direct reference? | Source of transitive packages bleeding into BFF publish? |
|---|---|---|---|
| `Spaarke.Core` | `src/server/shared/Spaarke.Core/` | Yes | YES — its package refs ship in BFF publish |
| `Spaarke.Dataverse` | `src/server/shared/Spaarke.Dataverse/` | Yes (also via Core) | YES — its package refs ship in BFF publish |

---

## Implications for Phase 4 candidate selection

1. **Cannot remove a package transitive bleed by editing only BFF csproj** — if a package comes in through `Spaarke.Core`, it requires editing `Spaarke.Core.csproj`.
2. Per UQ-06 default (inventory-only): edits to `Spaarke.Core` / `Spaarke.Dataverse` happen only when a Phase 4 candidate requires it. Wider audit deferred to follow-up project.
3. The Outcome E facade (Phase 4 task 046) creates `Sprk.Bff.Api/Services/Ai/PublicContracts/` — internal to BFF; no cross-project refactor.
