namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The "system mail" (secret-bearing) send option (unified-access-control-r2 task 185, owner round 98 Q4): a shared-mailbox
/// email whose body carries a secret its recipient alone must hold, such as an invite link with a one-time code. Set on
/// <see cref="SendCommunicationRequest.SystemMail"/>; absent on every other send, which is then unchanged.
/// </summary>
/// <remarks>
/// <para>When it is set, <see cref="CommunicationService"/>:</para>
/// <list type="number">
/// <item>does NOT run communication enrichment (no AI indexing, triage or proposals over the body);</item>
/// <item>persists <see cref="StoredBody"/> as <c>sprk_communication.sprk_body</c> instead of the sent body; and</item>
/// <item>sends with <c>SaveToSentItems = false</c>, so the shared mailbox keeps no copy.</item>
/// </list>
/// <para>The CALLER supplies the stored body, with the secret already removed (design note 185 §12 I-6). The communication
/// service never parses a body. Only the shared-mailbox email path accepts the option; any other send that carries it is
/// refused before anything is sent.</para>
/// <para>Internal and never JSON-bound: only server code can mark a send as system mail. A client that could set it could
/// store a body that differs from the one it sent.</para>
/// </remarks>
internal sealed record SystemMailOptions
{
    /// <summary>The body to persist on the communication record: the sent body with its secret replaced.</summary>
    public required string StoredBody { get; init; }
}
