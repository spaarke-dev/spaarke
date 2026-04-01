# Service Registration Pattern

## When
Registering new services in DI or adding a new feature module.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` — Core services (auth, cache)
2. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DocumentsModule.cs` — Feature module exemplar
3. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkersModule.cs` — Background workers module
4. `src/server/api/Sprk.Bff.Api/Program.cs` — Module registration calls (AddSpaarkeCore, AddDocumentsModule, etc.)

## Constraints
- **ADR-010**: Max 15 non-framework DI registrations total across all modules

## Key Rules
- One `Add{Module}Module()` extension method per feature area — called from Program.cs
- Singleton: stateless, thread-safe (GraphClientFactory, OpenAiClient, CacheMetrics)
- Scoped: per-request state, HttpContext deps (SpeFileStore, AuthorizationService, RequestCache)
- Use Options pattern with `ValidateDataAnnotations().ValidateOnStart()` — fail fast on bad config
- Concrete types by default — interfaces only when a seam is needed
