# Spike #1 — TipTap OOB + DOCX Round-Trip Prototype

> **THROWAWAY** — per POML constraint, this prototype lives in `notes/spikes/` ONLY.
> NOT promoted to `src/`. The binding artifact is `../spike-1-tiptap-docx-roundtrip.md`.

## Purpose

Validate that TipTap (StarterKit + standard open-source extensions) renders + edits + round-trips
representative legal DOCX content with an open-source DOCX bridge. Outputs:
1. Empirically-validated OOB feature inventory
2. DOCX bridge library choice (name + version + rationale)
3. Locked DOCX subset spec for R1

## Stack

- **Vite** (build) + **React 18** + **TypeScript**
- **TipTap 2.10.x** (StarterKit + 11 standard open-source extensions — see `package.json`)
- **mammoth ^1.8.0** (DOCX → HTML import)
- **docx ^9.0.3** (TipTap-JSON → DOCX export)

## Run

```bash
cd notes/spikes/spike-1-prototype
npm install --legacy-peer-deps --no-audit --no-fund
npm run dev
# Open http://localhost:5173, load a DOCX fixture, inspect rendering + export.
```

Per root CLAUDE.md §12, prefer `npm install --legacy-peer-deps --no-audit --no-fund` over `npm ci`.

## Files

| File | Purpose |
|---|---|
| `package.json` | TipTap OOB extension list + bridge libs (versions LOCKED for the inventory) |
| `src/Editor.tsx` | Editor wiring with ALL OOB extensions enabled — also documents R1 wiring contract |
| `src/exportDocx.ts` | TipTap JSON → DOCX (export side of the bridge) — also documents the export contract |
| `fixtures/README.md` | How to populate the 3 required fixtures (sanitized; not committed) |

## What this prototype proves

- TipTap StarterKit + 11 standard extensions cover the locked R1 OOB feature set
- mammoth.js produces clean, ProseMirror-compatible HTML on legal DOCXs (the import diff is *visible*)
- `docx` library can rebuild a DOCX from TipTap JSON for the OOB subset
- Round-trip is **lossy by design** for features outside OOB — and the loss is documented, not silent

## What this prototype does NOT do

- No production styling
- No SPE integration (BFF wiring belongs in R1 tasks, not the spike)
- No automated diff tool (manual visual inspection in Word for the fixture comparison)
- No comments, no tracked changes, no footnotes — those are *intentionally* out of OOB
