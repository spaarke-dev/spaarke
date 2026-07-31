# G1 Origin Marker — As-Built Dataverse Field (task 002 ✅)

> **Created by owner in Dataverse 2026-07-29.** Authoritative field contract for tasks 020 (G1 routing) + 040 (profile).

## Field
- **Table**: `sprk_document`
- **Column (logical)**: `sprk_composeorigin` · **Schema**: `sprk_ComposeOrigin` · **Display**: "Compose Origin"
- **Type**: Choice (local option set) · **Required**: Optional

## Option values (AS-BUILT — use these exact integers in code)
| Label | Value |
|---|---|
| **Authored** | `100000000` |
| **Imported** | `100000001` |
| **Default** | Imported (`100000001`) |

## Binding contract for task 020 (and any consumer)
- **Write (create-on-save, `ComposeService.SaveAsync`)**: born-in-editor → `100000000` (Authored); upload/browse/open-from-existing `.docx` → `100000001` (Imported).
- **Read (`ComposeService.LoadAsync`)**: return the value; client routes `Authored (100000000)` → clean payload; `Imported (100000001)` OR **`null`** → tracked op-log.
- **Null-handling (BINDING)**: pre-existing `sprk_document` rows return `null` (no backfill). Treat `null` as `Imported` — NEVER strict-equal to Authored. Defaults apply to new rows only.
- **No inference from SPE-id presence** — that fragile discriminator is exactly what G1 replaces.

## Provenance
Owner created the field via maker UI; initial Authored value was `1000000` (two zeros dropped), corrected to `100000000` on 2026-07-29 before any code/records depended on it (clean fix, no migration). Matches the Spaarke `100000000`-base choice convention (cf. `sprk_communicationtype`, `sprk_direction`).
