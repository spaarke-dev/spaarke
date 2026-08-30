# SUPERSEDED — see branch-protection-enabled-2026-08-29.md

> ⚠️ **SUPERSEDED 2026-08-29.** Branch protection is now ENABLED via ruleset 21824191
> (required check `Router`, strict=false, PR required). See `branch-protection-enabled-2026-08-29.md`.
>
> Two claims in this file are now known to be unsafe:
> 1. The 404 recorded here is NOT proof protection was deliberately switched off — the classic
>    `/branches/master/protection` endpoint returns the identical 404 when it simply does not serve
>    this repo, which is the case here (public repo, admin:true, repo-scoped token).
> 2. The stated forward target of `strict=true` was NOT adopted. It serializes merging (first merge
>    invalidates every other open PR) which is exactly north star #2. `strict=false` is in force.
>
> The rollback command below targets the non-functional classic endpoint. The real rollback is:
> `gh api -X DELETE repos/spaarke-dev/spaarke/rulesets/21824191`


Original snapshot retained unmodified at `branch-protection-pre-cutover.json`.
