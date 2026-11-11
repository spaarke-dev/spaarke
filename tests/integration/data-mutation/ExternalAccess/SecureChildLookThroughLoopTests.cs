// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194b fix round R (A3) — a primary parent that is a user-owned child filed under another such
//     row filed back under it (an event and its communication, two documents) is looked through once each; at the revisit
//     the resolver decides over every parent it holds, so Make Secure takes the loop and the rows under it into isolation
//     instead of refusing them.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL reconciler, resolver and synchronizer over task 148's world.

using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

public class SecureChildLookThroughLoopTests
{
    private const string Matter = "sprk_matter";

    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), webApi: null!);

    private Task<SecureChildReconcileReport> MakeSecureAsync(Guid matter) =>
        Reconciler().ReconcileAsync(Matter, matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

    [Fact(DisplayName = "194b R (A3) must fire: Make Secure on M — an event and its communication filed under each other (both user-owned, both on M) and a To Do under the event all move into isolation")]
    public async Task MakeSecure_AnEventCommunicationLoop_AndTheRowsUnderIt_MoveIn()
    {
        var (m, e, c, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(Matter, m)
            .UserOwnedChild("sprk_event", e, ("sprk_regardingcommunication", "sprk_communication", c), ("sprk_regardingmatter", Matter, m))
            .UserOwnedChild("sprk_communication", c, ("sprk_regardingevent", "sprk_event", e), ("sprk_regardingmatter", Matter, m))
            .OrdinaryChild("sprk_todo", todo, ("sprk_regardingevent", "sprk_event", e));

        var report = await MakeSecureAsync(m);

        foreach (var (table, id) in new[] { ("sprk_event", e), ("sprk_communication", c), ("sprk_todo", todo) })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureTeam), $"{table} sits under the secure matter");
            report.Changes.Should().ContainSingle(ch => ch.Id == id).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
        }
    }

    [Fact(DisplayName = "194b R (A3) must fire: Make Secure on M — two documents filed under each other (both user-owned, both on M) and an analysis of one move into isolation")]
    public async Task MakeSecure_ADocumentLoop_AndTheAnalysisUnderIt_MoveIn()
    {
        var (m, a, b, analysis) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(Matter, m)
            .UserOwnedChild("sprk_document", a, ("sprk_parentdocument", "sprk_document", b), ("sprk_matter", Matter, m))
            .UserOwnedChild("sprk_document", b, ("sprk_parentdocument", "sprk_document", a), ("sprk_matter", Matter, m))
            .OrdinaryChild("sprk_analysis", analysis, ("sprk_documentid", "sprk_document", a));

        var report = await MakeSecureAsync(m);

        foreach (var (table, id) in new[] { ("sprk_document", a), ("sprk_document", b), ("sprk_analysis", analysis) })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureTeam), $"{table} sits under the secure matter");
            report.Changes.Should().ContainSingle(ch => ch.Id == id).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
        }
    }
}
