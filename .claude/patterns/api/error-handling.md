# Error Handling Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Returning errors from API endpoints or adding global exception handling.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Errors/ProblemDetailsHelper.cs` — Validation, auth, Graph, AI error helpers
2. `src/server/api/Sprk.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs` — Custom exception with Code/Title/Status
3. `src/server/api/Sprk.Bff.Api/Program.cs` — Global exception handler (UseExceptionHandler section)

## Constraints
- **ADR-019**: All errors use RFC 7807 ProblemDetails format
- MUST include correlationId (from HttpContext.TraceIdentifier) in all error responses

## Key Rules
- Error codes by domain: `invalid_{field}` (validation), `sdap.access.deny.{reason}` (auth), `graph_error`, `obo_failed`, `ai_{type}`, `server_error`
- Throw `SdapProblemException` for business errors — global handler converts to ProblemDetails
- Use `ProblemDetailsHelper.FromGraphException()` for Graph SDK ODataError
- Never expose stack traces — return user-friendly messages
