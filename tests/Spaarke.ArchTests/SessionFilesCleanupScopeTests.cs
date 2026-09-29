using System.Reflection;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarkeai-compose-r8 FR-B03 (task 061) — structural fitness function: the 24h session-files cleanup
/// sweep may evict the HOT AI-Search index and NOTHING ELSE. It must have no reachable path to the
/// durable byte store.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a guard and not just a code review.</b> FR-B03's requirement is a NEGATIVE: a background job
/// that deletes data must not be able to delete a different store. Before task 061 that held only
/// because nobody had written <c>GetService&lt;SessionFileBlobStore&gt;()</c> inside a class that was
/// handed an <see cref="IServiceProvider"/> — an absence of code, which no review can keep true. Task 061
/// removed the reach; this file is what keeps it removed.
/// </para>
/// <para>
/// <b>Why it matters MORE after this task, not less.</b> Task 060 deliberately shipped
/// <c>SessionFileBlobStore</c> with no delete method at all, so even reaching it could not have destroyed
/// anything. Task 063 (GDPR erasure) adds the first delete surface. On the day that lands, this guard is
/// the only remaining thing standing between a 24h sweep and 90 days of user document bytes.
/// </para>
/// <para>
/// <b>Companion coverage.</b> The behavioural half lives in
/// <c>tests/integration/seam/Ai/SessionFilesCleanupHotIndexOnlySeamTests.cs</c>, which runs a real
/// eviction over a populated durable store and asserts the bytes survive. Structure without behaviour
/// proves the call is absent; behaviour without structure proves only today's build. Both are needed.
/// </para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions", this file is MAINTAIN-class: it is
/// the mechanism, not scaffolding.</para>
/// </remarks>
public class SessionFilesCleanupScopeTests
{
    private const string JobRelativePath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupJob.cs";

    private const string AccessRelativePath =
        "src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesHotIndexAccess.cs";

    private const string JobTypeName = "Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupJob";
    private const string AccessTypeName = "Sprk.Bff.Api.Services.Ai.Chat.SessionFilesHotIndexAccess";

    /// <summary>
    /// Names that would mean the sweep can see the durable byte store, or blob storage at all.
    /// </summary>
    private static readonly string[] DurableStoreTokens =
    {
        "SessionFileBlobStore",
        "SessionFileBlobGateway",
        "SessionFileBytes",
        "SessionFileRehydrationService",
        "Azure.Storage",
        "BlobServiceClient",
        "BlobContainerClient",
        "BlobClient",
    };

    /// <summary>
    /// Ambient-resolution constructs. Any one of these inside the job body re-opens the reach that
    /// task 061 closed, because from a container you can resolve anything.
    /// </summary>
    private static readonly string[] ServiceLocatorTokens =
    {
        "GetService<",
        "GetRequiredService<",
        "CreateScope(",
        "IServiceScopeFactory",
        "IServiceScope ",
        ".ServiceProvider",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. The job's SOURCE mentions neither blob storage nor a service locator.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CleanupJob_SourceContainsNoReferenceToTheDurableByteStore()
    {
        var codeText = ReadCodeText(JobRelativePath);
        var offenders = FindTokens(codeText, DurableStoreTokens);

        Assert.True(
            offenders.Count == 0,
            "SessionFilesCleanupJob must not name the durable session-file byte store or any Azure Blob " +
            "type. It sweeps on a 24h Redis-key expiry while the conversation those files belong to lives " +
            "90 days (spaarkeai-compose-r8 FR-B03) — deleting durable bytes on that schedule is the defect " +
            "Track B exists to remove, and it would surface weeks later as a user's file vanishing, not as " +
            "a failing test. Evict the AI-Search index only. Found: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void CleanupJob_SourceResolvesNothingFromTheContainer()
    {
        var codeText = ReadCodeText(JobRelativePath);
        var offenders = FindTokens(codeText, ServiceLocatorTokens);

        Assert.True(
            offenders.Count == 0,
            "SessionFilesCleanupJob must not resolve services from the container. Its constructor consumes " +
            "the IServiceProvider into SessionFilesHotIndexAccess and does not retain it, which is what " +
            "makes 'this sweep cannot reach the durable byte store' a property of the code rather than of " +
            "nobody having written the call yet (FR-B03). Add what you need to SessionFilesHotIndexAccess " +
            "— deliberately, and where a reviewer will see it. Found: " + string.Join(" | ", offenders));
    }

    /// <summary>
    /// Positive control. Without this, deleting the mechanism outright would make both guards above pass.
    /// </summary>
    [Fact]
    public void CleanupJob_StillRoutesItsDependenciesThroughTheNarrowedAccessType()
    {
        var codeText = ReadCodeText(JobRelativePath);

        Assert.Contains("SessionFilesHotIndexAccess.Resolve(", codeText, StringComparison.Ordinal);
        Assert.Contains("_hotIndexAccess", codeText, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. The job's compiled SHAPE holds nothing that can resolve a service.
    //    (Source scanning cannot see a field introduced via a partial class or a
    //    base type; reflection can.)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CleanupJob_HoldsNoFieldFromWhichAnyServiceCouldBeResolved()
    {
        var jobType = ResolveType(JobTypeName);

        var offenders = jobType
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => IsResolutionCapable(f.FieldType))
            .Select(f => $"{f.Name} : {f.FieldType.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "SessionFilesCleanupJob must retain no field capable of resolving services. A container " +
            "reference is a reach into every store in the process, including the durable session-file " +
            "bytes this sweep must never touch (FR-B03). Found: " + string.Join(" | ", offenders));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. The narrowed access type stays narrow.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole guarantee rests on this type exposing ONLY the hot tier. If it grows a third
    /// collaborator, the job's reach grows with it — silently, because every test above still passes.
    /// </summary>
    [Fact]
    public void HotIndexAccess_ExposesOnlyTheHotIndexAndTheReadOnlyRedisProbe()
    {
        var accessType = ResolveType(AccessTypeName);

        var exposed = accessType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "ActiveSessionKeys", "HotIndex", "IndexName" },
            exposed);
    }

    [Fact]
    public void HotIndexAccess_SourceContainsNoReferenceToTheDurableByteStore()
    {
        var codeText = ReadCodeText(AccessRelativePath);

        // NOTE: unlike the job, this file legitimately touches IServiceProvider — resolving from the
        // container once, in one auditable place, IS its purpose. What it must never do is resolve a
        // blob type, because that is the reach the job would inherit.
        var offenders = FindTokens(codeText, DurableStoreTokens);

        Assert.True(
            offenders.Count == 0,
            "SessionFilesHotIndexAccess defines the complete set of things the cleanup sweep can touch. " +
            "Adding a blob type here hands the durable byte store to a job whose whole contract is that it " +
            "cannot reach it (FR-B03). Found: " + string.Join(" | ", offenders));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsResolutionCapable(Type type)
        => typeof(IServiceProvider).IsAssignableFrom(type)
           || type.FullName is "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
                            or "Microsoft.Extensions.DependencyInjection.IServiceScope";

    private static Type ResolveType(string fullName)
    {
        // The BFF assembly is a ProjectReference of this test project, so it is already loaded by the
        // time any test runs — but reference it explicitly rather than relying on that.
        var assembly = typeof(Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupJob).Assembly;
        var type = assembly.GetType(fullName, throwOnError: false);

        Assert.True(type is not null,
            $"Type '{fullName}' was not found in {assembly.GetName().Name}. This guard is pinned to the " +
            "FR-B03 mechanism by name; if the type was renamed, update this file deliberately rather " +
            "than deleting the guard.");

        return type!;
    }

    private static string ReadCodeText(string relativePath)
    {
        var file = SourceScan.ServerSourceFiles().FirstOrDefault(f =>
            SourceScan.Relative(f).Replace('\\', '/')
                .EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));

        Assert.True(file is not null, $"Expected source file not found: {relativePath}");

        return SourceScan.CodeText(File.ReadAllLines(file!));
    }

    private static List<string> FindTokens(string codeText, IEnumerable<string> tokens)
    {
        var offenders = new List<string>();

        foreach (var token in tokens)
        {
            var idx = codeText.IndexOf(token, StringComparison.Ordinal);
            while (idx >= 0)
            {
                offenders.Add($"line {SourceScan.LineOf(codeText, idx)}: {token}");
                idx = codeText.IndexOf(token, idx + token.Length, StringComparison.Ordinal);
            }
        }

        return offenders;
    }
}
