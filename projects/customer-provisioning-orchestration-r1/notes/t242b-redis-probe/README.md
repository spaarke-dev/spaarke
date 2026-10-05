# T242b Redis live probe (2026-10-04)

Kept as `.txt` so no repo build picks it up. To rerun: copy both files to an empty folder, drop the `.txt`
suffixes, and run in a container that carries the identity under test (the cache has access keys disabled):

```
az container create ... --image mcr.microsoft.com/dotnet/sdk:10.0 --assign-identity <UAMI resource id> \
  --environment-variables REDIS_ENDPOINT=<host>:10000 UAMI_CLIENT_ID=<client id> AZURE_TENANT_ID=<tenant> ...
# then: dotnet run -c Release
```

It uses the BFF's resolved package versions and connection path (`CacheModule.BuildManagedIdentityOptions`:
DefaultAzureCredential pinned to the UAMI + tenant, Ssl, RESP3, `ConfigureForAzureWithTokenCredentialAsync`) and
checks: RESP3 survives the library call and is negotiated; a pub/sub round-trip; the
`MembershipCacheInvalidationSubscriber.EvictAsync` SCAN/DEL loop over `GetEndPoints()` removes 40 keys spread over
many hash slots.

Result on `spaarke-bff-redis-dev` (Balanced_B0, non-HA, OSSCluster) as `mi-bff-api-dev`: **PASS** — 2 primaries,
both RESP3; pub/sub received; 40/40 keys over 40 slots deleted; none left.
