# CommunicationPage (retired 2026-11-22)

This folder is the source of the `sprk_communicationpage` code page, moved here verbatim from `src/client/code-pages/CommunicationPage/` when it was retired (#2908). It is kept as an archive, not maintained, and is not built. `package.json` and `package-lock.json` carry an `.archived` suffix so that no install, build or audit treats the folder as a package.

## Why it was retired

The one live caller was the secure-child ribbon's "New Message" command (`sprk_secure_child_ribbon.js`), and the web resource was not deployed on DEV. That command is now "New Email": it opens the email composer (`sprk_emailpage`, the page `openEmailCompose` opens) with the host record as "Related to".

Its parts already live in maintained code:

| Part here | Maintained home |
|---|---|
| Compose mode (`EmailComposerSlot.tsx`) | `src/solutions/EmailPage` compose mode, via `openEmailCompose` |
| `ConnectionsEditor.tsx`, `provenance.ts` | `src/client/shared/Spaarke.Communication.Components/src/logic/connections/` and the `CommunicationConnections` PCF |
| `CommunicationHeader.tsx`, the read-only view | `EmailReadingPaneShell` in `@spaarke/communication-components` |

## Restore

```powershell
git mv docs/archive/code-pages/CommunicationPage src/client/code-pages/CommunicationPage
git mv src/client/code-pages/CommunicationPage/package.json.archived src/client/code-pages/CommunicationPage/package.json
git mv src/client/code-pages/CommunicationPage/package-lock.json.archived src/client/code-pages/CommunicationPage/package-lock.json
```
