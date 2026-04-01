# Send Email Integration Pattern

## When
Adding email sending capability to any module (UI, AI playbook, or server-side).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — Core send pipeline
2. `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` — REST endpoint (POST /api/communications/send)
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SendCommunicationToolHandler.cs` — AI tool handler
4. `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs` — Request model
5. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` — DI registration

## Constraints
- **ADR-001**: Extend BFF, not separate service
- **ADR-008**: Endpoint filter for auth
- **ADR-010**: Register in feature module, not CommunicationModule
- **ADR-013**: AI tool uses same CommunicationService pipeline

## Key Rules
- Three patterns: UI -> `POST /api/communications/send` | AI Playbook -> `send_communication` tool | Server -> inject `CommunicationService`
- Default sender: `fromMailbox: null` uses shared mailbox (`SendMode.SharedMailbox`)
- Always set `associations` to link email to entity (matter, account, contact)
- Handle 429 (`DAILY_SEND_LIMIT_REACHED`) gracefully in UI
