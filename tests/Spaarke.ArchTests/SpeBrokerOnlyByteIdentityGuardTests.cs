using System.Reflection;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 171 (owner rounds 69 + 70) — structural fitness functions for broker-only SPE bytes.
/// </summary>
/// <remarks>
/// <para><b>Rule 1 — who may GRANT a user an SPE container role.</b> Exactly two types reference Graph's
/// container-permission create: <c>SpeContainerMembershipService</c> (the MARKED grants: just-in-time Office edit on a
/// secure container, standing business-unit writers — round 70 — and, since adversarial finding 6, self-registration
/// Step 8, which now grants through it) and <c>SpeAdminGraphService</c> (the SPE admin console's explicit, admin-gated
/// permission route). A third caller is a new way for access to stop following Dataverse, so it fails here.</para>
/// <para><b>Rule 2 — who may still read or write SPE bytes AS THE USER.</b> Every record-backed byte path is app-only
/// behind a Dataverse decision. The OBO byte members left on the facade serve only paths with no Dataverse record
/// behind them (task 171 escalation trigger 2, reported to the owner): Compose "Path B" through
/// <c>ComposeSpeAccess</c>, the chat-session check that authorizes those sessions, RAG indexing of an item named
/// without a document, and the configured-staging uploads. A new caller must authorize a record and use the app-only
/// member instead — or be added here with the owner's decision.</para>
/// <para>Read from compiled IL (<see cref="IlCallScan"/>), so a receiver's type is known exactly; the scan's own stated
/// limits (reflection, late binding) apply. MAINTAIN-class (tests/CLAUDE.md "Structural fitness functions").</para>
/// </remarks>
public class SpeBrokerOnlyByteIdentityGuardTests
{
    private static readonly Assembly Bff = typeof(SpeFileStore).Assembly;

    private static IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> References()
        => IlCallScan.MethodReferences(Bff.GetTypes());

    [Fact(DisplayName = "Task 171: only the marked-grant service and the SPE admin console create SPE container permissions")]
    public void OnlyTwoTypesCreateContainerPermissions()
    {
        var granters = References()
            .Where(r => r.Target.Name == "PostAsync"
                        && r.Target.DeclaringType?.FullName?.EndsWith(".Containers.Item.Permissions.PermissionsRequestBuilder", StringComparison.Ordinal) == true)
            .Select(r => IlCallScan.Outermost(r.Caller).FullName)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            granters.SequenceEqual(new[] {
                "Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService",
                "Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService",
            }.OrderBy(n => n, StringComparer.Ordinal)),
            "a user is granted an SPE container role only by the marked JIT / standing grants (round 69 / 70 — self-"
            + "registration Step 8 included) or the admin console — anything else lets access drift from Dataverse" + " Found: " + string.Join(", ", granters));
    }

    /// <summary>The OBO byte members of the facade (task 171 left exactly these).</summary>
    private static readonly string[] OboByteMembers =
    [
        "GetFileMetadataAsUserAsync",
        "DownloadFileAsUserAsync",
        "DownloadFileVersionAsUserAsync",
        "GetCurrentVersionIdAsUserAsync",
        "ReplaceFileContentAsUserAsync",
        "UploadSmallToStagingAsUserAsync",
    ];

    [Fact(DisplayName = "Task 171: the facade's OBO byte members are called only from the record-less paths the owner was told about")]
    public void OboByteMembers_AreCalledOnlyFromTheRecordlessPaths()
    {
        var facade = new[] { typeof(SpeFileStore), typeof(ISpeFileOperations), typeof(DriveItemOperations), typeof(UploadSessionManager) };

        var callers = References()
            .Where(r => r.Target.DeclaringType is { } t && facade.Contains(t) && OboByteMembers.Contains(r.Target.Name))
            .Select(r => IlCallScan.Outermost(r.Caller))
            .Where(t => !facade.Contains(t))
            .Select(t => t.FullName)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            callers.SequenceEqual(new[] {
                // Compose "Path B" (no sprk_document row, or a row whose pointer does not verify) — the ONE identity
                // rule for every Compose byte call...
                "Sprk.Bff.Api.Services.Compose.ComposeSpeAccess",
                // ...and the same rule at create-on-save's transient-key hit (no route-level mark exists there).
                "Sprk.Bff.Api.Services.Compose.ComposeService",
                // The stored chat-session SPE item of a Path B Compose session.
                "Sprk.Bff.Api.Api.Filters.AiAuthorizationFilter",
                // /api/ai/rag/index-file naming a drive item with no document.
                "Sprk.Bff.Api.Services.Ai.FileIndexingService",
                // Configured-staging uploads (chat persist, chat Word export). Workspace pre-fill no longer stages: it
                // extracts in memory since customer-provisioning-orchestration-r1 task 227f retired its staging container.
                "Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints",
                "Sprk.Bff.Api.Api.Ai.ChatWordExportEndpoints",
            }.OrderBy(n => n, StringComparer.Ordinal)),
            "every byte path with a Dataverse record behind it runs app-only after that record's decision (owner round "
            + "69); an OBO byte call works only for a caller who holds a container role, which per-record secure "
            + "containers never grant" + " Found: " + string.Join(", ", callers));
    }
}
