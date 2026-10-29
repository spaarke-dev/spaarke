using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 194 F1-b: a budget's buckets (<c>sprk_budgetbucket.sprk_budget</c>) are the budget's
/// secure family. Before, the budget under a Secure matter was isolated and its buckets were not. Driven over the REAL
/// <see cref="SecureChildReconciler"/>, the REAL ownership resolver and one in-memory Dataverse (<see cref="SecureChildShareWorld"/>).
/// </summary>
public class SecureChildBudgetBucketLineageTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid Author = SecureChildShareWorld.SomeUser;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

    /// <summary>A matter root's pass reads its Assign cascade (document locations) over the Web API; this one has none.</summary>
    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    [Fact(DisplayName = "194 F1-b must fire: a matter turning Secure takes its budget AND the budget's buckets into isolation")]
    public async Task AMatterTurningSecure_ReownsItsBudget_AndTheBudgetsBuckets()
    {
        var (matter, budget, bucket1, bucket2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild("sprk_budget", budget, ("sprk_matter", "sprk_matter", matter))
            .UserOwnedChild("sprk_budgetbucket", bucket1, ("sprk_budget", "sprk_budget", budget))
            .UserOwnedChild("sprk_budgetbucket", bucket2, ("sprk_budget", "sprk_budget", budget));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_budget", budget).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _world.OwnerOf("sprk_budgetbucket", bucket1).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "a bucket is filed under its budget, so it is isolated with it");
        _world.OwnerOf("sprk_budgetbucket", bucket2).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    [Fact(DisplayName = "194 F1-b must not fire: a bucket of a budget on an ordinary matter keeps its owner")]
    public async Task ABucketOfABudgetOnAnOrdinaryMatter_IsUntouched()
    {
        var (secureMatter, ordinaryMatter, budget, bucket) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", secureMatter)
            .OrdinaryRoot("sprk_matter", ordinaryMatter)
            .UserOwnedChild("sprk_budget", budget, ("sprk_matter", "sprk_matter", ordinaryMatter))
            .UserOwnedChild("sprk_budgetbucket", bucket, ("sprk_budget", "sprk_budget", budget));

        await Reconciler().ReconcileAsync(
            "sprk_matter", secureMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_budgetbucket", bucket).Should().Be(DataversePrincipalRef.User(Author));
        _world.OwnerWrites.Should().BeEmpty();
    }
}
