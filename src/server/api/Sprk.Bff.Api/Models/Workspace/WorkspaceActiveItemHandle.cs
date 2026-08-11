namespace Sprk.Bff.Api.Models.Workspace;

/// <summary>
/// spaarkeai-assistant-enhancements-r3 task 011 (FR-04, server half) — the client-supplied
/// active-item handle threaded into the single active-item slot of the workspace-state prompt
/// block (<see cref="Services.Ai.Chat.SprkChatAgentFactory.BuildWorkspaceStateBlock"/>).
///
/// <para>
/// <b>ADR-015 (Path A, honest) — id-not-content BINDING</b>: this handle carries EXACTLY
/// <c>{ id, type, label }</c> and NOTHING else. It is a thin identity slice: <see cref="Id"/> is
/// the opaque fetch key the Assistant passes to a downstream parity tool to retrieve the real
/// content, <see cref="Type"/> names the kind of thing (e.g. <c>"email"</c>, <c>"document"</c>),
/// and <see cref="Label"/> is a human-readable title. NO item content (body, snippet, selection
/// text, row data, chart data) may ever be added to this record — a content field here is a
/// governance defect (proven absent by a structural/snapshot test). All real content is
/// tool-fetched by <see cref="Id"/> from the source of record.
/// </para>
///
/// <para>
/// Published client-side by the task-001 active-item conduit (a generalization of the Compose
/// <c>composeActionBridge</c>) and carried on the chat request; mapped here from the request DTO
/// at the ChatEndpoints call site. Defined in the Models layer so both the API layer
/// (<c>ChatEndpoints</c>) and the Services layer (<c>SprkChatAgentFactory</c>) can reference it
/// without a Services→Api dependency.
/// </para>
/// </summary>
/// <param name="Id">Opaque, deterministic fetch key for the active item (NEVER user text). All content is retrieved by this id downstream.</param>
/// <param name="Type">The kind of item (e.g. <c>"email"</c>, <c>"document"</c>) — an identity discriminator, not content.</param>
/// <param name="Label">Human-readable title of the active item — a thin identity slice, never a body/snippet.</param>
public sealed record WorkspaceActiveItemHandle(string Id, string Type, string Label);
