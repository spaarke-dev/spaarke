using System.Text.Json;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// The serializer configuration for the small JSON payloads Compose keeps in the distributed cache
/// (ADR-009 Redis): the save-path version stamp and the PDF provenance markers.
/// </summary>
/// <remarks>
/// <para><b>Why this exists as its own type</b> (task 070). It was
/// <c>ComposeService.SaveStampJsonOptions</c> — private, and named for the save stamp it was introduced
/// for even though the PDF markers had started using it too. The decomposition split its consumers
/// across two collaborators (<see cref="ComposeSaveStorageCoordinator"/> for the stamp,
/// <see cref="ComposePdfIntakeCoordinator"/> for the markers), which left three options: leave it on the
/// class both were extracted FROM (a permanent reach-back into a former parent), give it to one of them
/// and let the other reach sideways, or duplicate it.</para>
///
/// <para>The last is the one that actually costs something: two cache-payload serializer configurations,
/// free to drift apart silently, deserializing each other's entries. That is the concrete failure this
/// single definition prevents, and it is why one shared field earns a file rather than being inlined
/// twice (CLAUDE.md §11 — the cost-of-doing-nothing question).</para>
///
/// <para>Renamed in the move: "save stamp" under-described a setting the PDF markers had used since
/// task 044.</para>
/// </remarks>
internal static class ComposeCacheJson
{
    /// <summary>Web defaults (camelCase) — the shape every persisted Compose cache payload is written
    /// and read with. Changing it is a WIRE-FORMAT change: entries written by the previous
    /// configuration are still live in Redis under their TTL, so any change must stay
    /// read-compatible with them or the read paths must tolerate the miss (they all do — every
    /// consumer degrades to "no marker / no stamp", never to an error).</summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
