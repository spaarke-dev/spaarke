# Archived: `@spaarke/sdap-client` package docs

These five files were the package-root docs of `src/client/shared/Spaarke.SdapClient` (`@spaarke/sdap-client`). That package was folded into `@spaarke/ui-components` on 2026-11-24 (DEF-001, #2801; ADR-012 Path B). They were moved here instead of deleted.

**They are historical and wrong in places.** Do not take an API, a size limit or an auth pattern from them:

- They describe a 4 MB small/chunked upload split. The client uses one Graph simple PUT up to 250 MB (FAILURE-MODES AP-12).
- They describe container-keyed uploads, drive-keyed downloads and deletes, and metadata reads. All of those routes are retired on the server, and the client methods are deleted.
- Their code samples use auth types that no longer exist. Auth is `authenticatedFetch` from `@spaarke/auth` (ADR-028).

The current client is `src/client/shared/Spaarke.UI.Components/src/services/sdap/`. Its header comments are the source of truth.
