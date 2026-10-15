using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// WHO an app-only upload was made for — the server-side binding the pointer attach needs now that every upload is
/// written by the BFF identity (unified-access-control-r2 task 171 attach fix; live regression on dev 2026-10-07).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <c>POST /api/v1/documents/{id}/file</c> lets the row's creator attach the file THEY uploaded, and
/// nothing else: a Write holder must not attach someone else's file and gain read on it. It used to read the uploader
/// from Graph's <c>createdBy.user</c>. Since task 171 the record-keyed and record-less uploads are app-only, so
/// <c>createdBy</c> is the Spaarke application for EVERY upload — "any item the BFF uploaded" would admit anyone's
/// file. The person is known only to the BFF, at the moment it uploads, so it records the binding then.</para>
/// <para><b>One binding, tied to the ITEM.</b> The small PUT routes see the item Graph created and record
/// <c>{itemId}</c> → (the caller's Entra object id, the drive, the time) in the caller's own TENANT's cache partition
/// (<see cref="ITenantCache"/>, Redis — ADR-009), for <see cref="Ttl"/>. There is no path- or name-based binding: a
/// binding recorded BEFORE an item exists (an upload session) cannot say which item it was for, and could be matched
/// by another user's file at that path (verifier F1, 2026-10-07). An item uploaded through an upload session therefore
/// carries no binding and cannot be attached through <c>/file</c> (the session route has no client).</para>
/// <para><b>Fail closed.</b> A cache fault on read propagates (the attach refuses with "try again"); a fault on write
/// fails the upload (and the just-uploaded item is deleted, best effort). A binding is removed once its attach
/// succeeded.</para>
/// <para><b>Why a class of its own</b> (CLAUDE.md §11). Existing: nothing records who an app-only upload was for (the
/// row's <c>sprk_createdbyperson</c> is the DOCUMENT's creator, not the file's uploader). Extension: it is a thin key
/// scheme over the existing tenant-scoped <see cref="ITenantCache"/>; no table, no column, no system-level key. Cost of
/// doing nothing: every Document Upload Wizard upload ends with a row that has no file (the regression).</para>
/// </remarks>
public class UploadAttribution
{
    /// <summary>How long an upload stays attachable by its uploader without an attach.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private const string Resource = "spe-upload-attribution";
    private const int Version = 1;

    private readonly ITenantCache _cache;
    private readonly TimeProvider _time;

    public UploadAttribution(ITenantCache cache, TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>One binding: the caller's Entra object id (normalized, <see cref="NormalizeCaller"/>), the drive, and when.</summary>
    public sealed record Binding(string Caller, string Drive, DateTimeOffset At);

    /// <summary>The outcome of <see cref="MatchAsync"/>.</summary>
    public enum MatchOutcome
    {
        /// <summary>A binding names the caller for this item, in this drive.</summary>
        Caller,

        /// <summary>A binding names someone else.</summary>
        OtherCaller,

        /// <summary>No binding for this item in this drive.</summary>
        None,
    }

    /// <summary>An object id as the binding stores it: GUID "D" form when it parses, else the trimmed value (never matches an attach).</summary>
    internal static string NormalizeCaller(string callerObjectId)
        => Guid.TryParse(callerObjectId, out var g) ? g.ToString("D") : callerObjectId.Trim();

    /// <summary>Records that <paramref name="item"/> in <paramref name="drive"/> was uploaded for the caller, in <paramref name="tenantId"/>'s partition.</summary>
    public virtual Task RecordItemAsync(string tenantId, string callerObjectId, string drive, string item, CancellationToken ct = default)
        => _cache.SetAsync(tenantId, Resource, Key(item), Version,
            new Binding(NormalizeCaller(callerObjectId), drive.Trim(), _time.GetUtcNow()), Ttl, ct: ct);

    /// <summary>Was <paramref name="item"/> in <paramref name="drive"/> uploaded for <paramref name="caller"/>? Faults propagate — the caller refuses.</summary>
    public virtual async Task<MatchOutcome> MatchAsync(
        string tenantId, Guid caller, string drive, string item, CancellationToken ct = default)
    {
        var binding = await _cache.GetAsync<Binding>(tenantId, Resource, Key(item), Version, ct: ct).ConfigureAwait(false);
        if (binding is null || !RecordContainerResolver.IsSameContainerId(binding.Drive, drive))
        {
            return MatchOutcome.None;
        }

        return caller != Guid.Empty && string.Equals(binding.Caller, caller.ToString("D"), StringComparison.OrdinalIgnoreCase)
            ? MatchOutcome.Caller
            : MatchOutcome.OtherCaller;
    }

    /// <summary>Removes the binding once its attach succeeded (best effort: it expires anyway).</summary>
    public virtual async Task ConsumeAsync(string tenantId, string item, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(tenantId, Resource, Key(item), Version, ct: ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The attach is done and the row now names the item; the attach's own "first file only" rule makes a repeat
            // a no-op, and no OTHER way of attaching consults this binding. It expires with the TTL.
        }
    }

    private static string Key(string item) => item.Trim();
}
