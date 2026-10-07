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
/// <para><b>Two bindings, both short-lived</b> (<see cref="Ttl"/>, the distributed cache — Redis, ADR-009 — keyed per
/// item or per path, value = the caller's Entra object id and the time):</para>
/// <list type="bullet">
/// <item><b>Item</b> — the small PUT routes see the created item in Graph's response: <c>item:{itemId}</c>.</item>
/// <item><b>Path</b> — an upload SESSION is created before the item exists, so the BFF records the exact target
/// (drive + path; the session is opened with conflict behaviour <c>fail</c>, so the name cannot change):
/// <c>path:{drive}:{path}</c>. The attach matches the item's folder + name and requires the item to be created after the
/// session was opened — and an item that carries an ITEM binding for someone else is never matched by path.</item>
/// </list>
/// <para><b>Fail closed.</b> A cache fault on read propagates (the attach refuses with "try again"); a fault on write
/// fails the upload. A binding is removed once the attach succeeds.</para>
/// <para><b>Why a class of its own</b> (CLAUDE.md §11). Existing: nothing records who an app-only upload was for (the
/// row's <c>sprk_createdbyperson</c> is the DOCUMENT's creator, not the file's uploader). Extension: it is a thin key
/// scheme over the existing <see cref="ITenantCache"/>; no table, no column. Cost of doing nothing: every Document Upload
/// Wizard upload ends with a row that has no file (the regression).</para>
/// </remarks>
public class UploadAttribution
{
    /// <summary>How long an upload stays attachable by its uploader without an attach.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    /// <summary>Graph's and the BFF's clocks may differ by this much when a session item's creation time is compared.</summary>
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private const string Partition = "spe";
    private const string Resource = "upload-attribution";
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

    /// <summary>An object id as the binding stores it: GUID "D" form when it parses, else the trimmed value (never matches an attach).</summary>
    internal static string NormalizeCaller(string callerObjectId)
        => Guid.TryParse(callerObjectId, out var g) ? g.ToString("D") : callerObjectId.Trim();

    /// <summary>The outcome of <see cref="MatchAsync"/>.</summary>
    public enum MatchOutcome
    {
        /// <summary>A binding names the caller for this item.</summary>
        Caller,

        /// <summary>A binding names someone else.</summary>
        OtherCaller,

        /// <summary>No binding matches.</summary>
        None,
    }

    /// <summary>Records that <paramref name="item"/> in <paramref name="drive"/> was uploaded for <paramref name="caller"/>.</summary>
    public virtual Task RecordItemAsync(string callerObjectId, string drive, string item, CancellationToken ct = default)
        => _cache.SetAsync(Partition, Resource, ItemKey(item), Version,
            new Binding(NormalizeCaller(callerObjectId), drive.Trim(), _time.GetUtcNow()), Ttl, ct: ct);

    /// <summary>Records that an upload session to <paramref name="path"/> in <paramref name="drive"/> was opened for <paramref name="caller"/>.</summary>
    public virtual Task RecordSessionAsync(string callerObjectId, string drive, string path, CancellationToken ct = default)
        => _cache.SetAsync(Partition, Resource, PathKey(drive, path), Version,
            new Binding(NormalizeCaller(callerObjectId), drive.Trim(), _time.GetUtcNow()), Ttl, ct: ct);

    /// <summary>
    /// Was <paramref name="item"/> (with Graph's <paramref name="facts"/>) uploaded for <paramref name="caller"/>? Returns
    /// the cache key to consume on success. Faults propagate — the caller refuses.
    /// </summary>
    public virtual async Task<(MatchOutcome Outcome, string? Key)> MatchAsync(
        Guid caller, string drive, string item, Sprk.Bff.Api.Models.SpeItemCreator facts, CancellationToken ct = default)
    {
        var itemKey = ItemKey(item);
        var byItem = await _cache.GetAsync<Binding>(Partition, Resource, itemKey, Version, ct: ct).ConfigureAwait(false);
        if (byItem is not null)
        {
            if (!RecordContainerResolver.IsSameContainerId(byItem.Drive, drive))
                return (MatchOutcome.None, null);
            return IsCaller(byItem, caller) ? (MatchOutcome.Caller, itemKey) : (MatchOutcome.OtherCaller, null);
        }

        // An upload-session item: match the session the BFF opened for its exact folder + name.
        if (ItemPath(facts) is not { } itemPath)
            return (MatchOutcome.None, null);

        var pathKey = PathKey(drive, itemPath);
        var byPath = await _cache.GetAsync<Binding>(Partition, Resource, pathKey, Version, ct: ct).ConfigureAwait(false);
        if (byPath is null || !RecordContainerResolver.IsSameContainerId(byPath.Drive, drive))
            return (MatchOutcome.None, null);
        if (!IsCaller(byPath, caller))
            return (MatchOutcome.OtherCaller, null);

        // The item must be the one the session created — not an item that already stood at that path.
        return facts.Created is { } created && created >= byPath.At - ClockSkew
            ? (MatchOutcome.Caller, pathKey)
            : (MatchOutcome.None, null);
    }

    /// <summary>Removes a binding once its attach succeeded (best effort: it expires anyway).</summary>
    public virtual async Task ConsumeAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(Partition, Resource, key, Version, ct: ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The attach is done; a binding that could not be removed only lets the SAME caller re-attach the SAME item
            // until it expires — which the attach's own "first file only" rule already makes a no-op.
        }
    }

    private static bool IsCaller(Binding binding, Guid caller)
        => caller != Guid.Empty && string.Equals(binding.Caller, caller.ToString("D"), StringComparison.OrdinalIgnoreCase);

    private static string ItemKey(string item) => "item:" + item.Trim();

    internal static string PathKey(string drive, string path)
        => "path:" + drive.Trim() + ":" + NormalizePath(path);

    /// <summary>A drive-relative path, without leading/trailing separators, case-folded (SharePoint names are case-insensitive).</summary>
    internal static string NormalizePath(string path)
        => path.Replace('\\', '/').Trim().Trim('/').ToUpperInvariant();

    /// <summary>
    /// The item's drive-relative path from Graph's <c>parentReference.path</c> (<c>/drives/{id}/root:</c> or
    /// <c>/drives/{id}/root:/folder</c>) and its name; <see langword="null"/> when either is missing or unrecognised.
    /// </summary>
    internal static string? ItemPath(Sprk.Bff.Api.Models.SpeItemCreator facts)
    {
        if (string.IsNullOrWhiteSpace(facts.Name) || string.IsNullOrWhiteSpace(facts.ParentPath))
            return null;
        var marker = facts.ParentPath.IndexOf("root:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var folder = Uri.UnescapeDataString(facts.ParentPath[(marker + "root:".Length)..]).Trim('/');
        return folder.Length == 0 ? facts.Name : folder + "/" + facts.Name;
    }
}
