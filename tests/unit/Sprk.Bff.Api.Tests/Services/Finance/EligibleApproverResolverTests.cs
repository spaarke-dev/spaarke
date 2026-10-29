using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 103 (FR-11, FR-14, D-33, D-54, D-78): <see cref="EligibleApproverResolver"/> and <see cref="EligibleApproverRefreshJob"/> over
/// the in-memory Dataverse (<see cref="ApprovalWorld"/>): who is eligible per core type, the settings default when the role is empty, the people
/// who contribute nobody, and the stored set the "Invoices to review" view filters on.
/// </summary>
public sealed class EligibleApproverResolverTests
{
    public static IEnumerable<object[]> CoreTypes() => [["sprk_matter"], ["sprk_project"], ["sprk_workassignment"]];

    private static void Roles(ApprovalWorld w, params BillingApproverRole[] roles) =>
        w.Core["sprk_billingapproverrole"] = new OptionSetValueCollection(roles.Select(r => new OptionSetValue((int)r)).ToList());

    private static Task<EligibleApproverSet> Resolve(ApprovalWorld w) => w.Resolver().ResolveAsync(w.CoreEntity, w.CoreId, CancellationToken.None);

    // ── Who is eligible ──

    [Theory(DisplayName = "103 FR-14: on every core type, a record with roles set resolves exactly the users in the selected assignment fields")]
    [MemberData(nameof(CoreTypes))]
    public async Task RolesSet_ResolveTheUsersInTheSelectedFields(string coreEntity)
    {
        var w = new ApprovalWorld(coreEntity);
        w.Core["sprk_assignedattorney2"] = new EntityReference("contact", Carol);
        Roles(w, BillingApproverRole.AssignedAttorney2, BillingApproverRole.AssignedToInternal);

        var set = await Resolve(w);

        set.FromDefault.Should().BeFalse();
        set.Roles.Should().Equal(BillingApproverRole.AssignedAttorney2, BillingApproverRole.AssignedToInternal);
        set.Approvers.Select(a => (a.ContactId, a.SystemUserId, a.Name)).Should().Equal((Bob, BobUser, "Bob Brown"), (Carol, CarolUser, "Carol Clark"));
        set.For(AliceUser).Should().BeNull("Assigned Attorney 1 is not a selected role on this record");
    }

    [Theory(DisplayName = "103 FR-14 / D-54: on every core type, an empty role resolves the settings default FOR THAT TYPE")]
    [MemberData(nameof(CoreTypes))]
    public async Task EmptyRole_ResolvesTheSettingsDefaultForItsType(string coreEntity)
    {
        var w = new ApprovalWorld(coreEntity);
        // A different default per type proves the type's own default is used, not another type's.
        w.SettingsValue = new EbillingSettings
        {
            MatterApproverRoles = [BillingApproverRole.AssignedAttorney1],
            ProjectApproverRoles = [BillingApproverRole.AssignedToInternal],
            WorkAssignmentApproverRoles = [BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal],
        };

        var set = await Resolve(w);

        set.FromDefault.Should().BeTrue();
        var expected = coreEntity switch
        {
            "sprk_matter" => new[] { AliceUser },
            "sprk_project" => [BobUser],
            _ => [AliceUser, BobUser],
        };
        set.Approvers.Select(a => a.SystemUserId).Should().BeEquivalentTo(expected);
    }

    [Fact(DisplayName = "103: the built-in default (no settings row) is Assigned Attorney 1 + Assigned To Internal")]
    public async Task BuiltInDefault_IsAttorney1AndToInternal()
    {
        var set = await Resolve(new ApprovalWorld());

        set.Roles.Should().Equal(BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal);
        set.Approvers.Select(a => a.SystemUserId).Should().Equal(AliceUser, BobUser);
    }

    [Fact(DisplayName = "103: a single-select value (the pre-DEF-011 Matter shape) is read as one role, not as empty")]
    public async Task SingleSelectValue_IsOneRole()
    {
        var w = new ApprovalWorld("sprk_matter");
        w.Core["sprk_billingapproverrole"] = new OptionSetValue((int)BillingApproverRole.AssignedToInternal);

        var set = await Resolve(w);

        set.FromDefault.Should().BeFalse();
        set.Approvers.Select(a => a.SystemUserId).Should().Equal(BobUser);
    }

    [Theory(DisplayName = "103 DEF-011: 'Assigned To' names nobody on Matter and Project (no such field) and the assigned person on Work Assignment")]
    [MemberData(nameof(CoreTypes))]
    public async Task AssignedTo_ExistsOnWorkAssignmentOnly(string coreEntity)
    {
        var w = new ApprovalWorld(coreEntity);
        w.Core["sprk_assignedto"] = new EntityReference("contact", Carol);
        Roles(w, BillingApproverRole.AssignedTo);

        var set = await Resolve(w);

        if (coreEntity == "sprk_workassignment")
        {
            set.Approvers.Select(a => a.SystemUserId).Should().Equal(CarolUser);
        }
        else
        {
            set.NoEligibleApprover.Should().BeTrue();
        }
    }

    [Fact(DisplayName = "103 FR-14: a contact with no user, an inactive contact and an empty field contribute nobody; zero eligible is flagged and logged, never 'anyone'")]
    public async Task PeopleWithoutAUser_ContributeNobody_AndZeroIsFlagged()
    {
        var w = new ApprovalWorld();
        w.Put(Contact(Bob, "Bob Brown", BobUser, active: false));
        Roles(w, BillingApproverRole.AssignedParalegal1, BillingApproverRole.AssignedToInternal, BillingApproverRole.AssignedParalegal2);

        var set = await Resolve(w);

        set.Approvers.Should().BeEmpty("Dan has no user, Bob is inactive, Paralegal 2 is empty");
        set.NoEligibleApprover.Should().BeTrue();
        set.For(BobUser).Should().BeNull();
        w.ResolverLog.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("no eligible approver"));
    }

    [Fact(DisplayName = "103: an unknown role value names no field and is NOT replaced by the default")]
    public async Task UnknownRoleValue_NamesNobody()
    {
        var w = new ApprovalWorld();
        w.Core["sprk_billingapproverrole"] = new OptionSetValueCollection([new OptionSetValue(999)]);

        var set = await Resolve(w);

        set.FromDefault.Should().BeFalse();
        set.NoEligibleApprover.Should().BeTrue();
    }

    [Fact(DisplayName = "103: a person named in two fields is one approver")]
    public async Task OnePersonInTwoFields_IsOneApprover()
    {
        var w = new ApprovalWorld();
        w.Core["sprk_assignedtointernal"] = new EntityReference("contact", Alice);

        var set = await Resolve(w);

        set.Approvers.Should().ContainSingle().Which.SystemUserId.Should().Be(AliceUser);
    }

    [Fact(DisplayName = "103: a table outside the core-record set resolves nobody without a read; a read fault throws (the caller decides)")]
    public async Task UnknownCoreType_IsNobody_AndAFaultThrows()
    {
        var w = new ApprovalWorld();

        var outside = await w.Resolver().ResolveAsync("account", Guid.NewGuid(), CancellationToken.None);
        outside.NoEligibleApprover.Should().BeTrue();
        w.Entities.Invocations.Should().BeEmpty();

        w.Faults["read:contact"] = new InvalidOperationException("dataverse down");
        var act = () => Resolve(w);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── The stored set (D-78) ──

    [Fact(DisplayName = "103 D-78: sync adds only the missing approvers, removes only the stale ones and stamps the time, in one transaction; an unchanged set writes nothing")]
    public async Task Sync_WritesOnlyTheDiff()
    {
        var w = new ApprovalWorld();
        var resolver = w.Resolver();
        var first = await resolver.SyncAsync(w.ApprovalId, await Resolve(w), resolvedOn: null, CancellationToken.None);

        first.Should().Be(new EligibleApproverSync(2, 0, Stamped: true));
        w.StoredApprovers.Should().BeEquivalentTo([Alice, Bob]);
        w.Portion.GetAttributeValue<DateTime>("sprk_eligibleresolvedon").Should().Be(Now.UtcDateTime);
        w.WriteLog.Should().Equal("tx:approvers");

        // Re-assigned: Attorney 1 is now Carol. Alice goes, Carol comes, Bob stays.
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);
        var second = await resolver.SyncAsync(w.ApprovalId, await Resolve(w), Now.UtcDateTime, CancellationToken.None);
        second.Should().Be(new EligibleApproverSync(1, 1, Stamped: true));
        w.StoredApprovers.Should().BeEquivalentTo([Bob, Carol]);

        var third = await resolver.SyncAsync(w.ApprovalId, await Resolve(w), Now.UtcDateTime, CancellationToken.None);
        third.Should().Be(new EligibleApproverSync(0, 0, Stamped: false));
        w.WriteLog.Should().HaveCount(2, "an unchanged, stamped set writes nothing");
    }

    [Fact(DisplayName = "103 D-78: 049's acceptance step stores the approvers of the invoice's Pending portion; nothing for an invoice not In Review")]
    public async Task RefreshInvoice_StoresAtAcceptance_OnlyWhileInReview()
    {
        var w = new ApprovalWorld();
        (await w.Resolver().RefreshInvoiceAsync(w.InvoiceId, CancellationToken.None)).Should().BeTrue();
        w.StoredApprovers.Should().BeEquivalentTo([Alice, Bob]);

        var approved = new ApprovalWorld(invoiceStatus: InvoiceStatusCodes.Approved);
        (await approved.Resolver().RefreshInvoiceAsync(approved.InvoiceId, CancellationToken.None)).Should().BeFalse();
        approved.Writes.Should().Be(0);
    }

    // ── The refresh job (ADR-036) ──

    private static EligibleApproverRefreshJob Job(ApprovalWorld w, bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(w.Entities.Object);
        services.AddScoped(_ => w.Resolver());
        return new EligibleApproverRefreshJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EbillingOptions { Enabled = enabled }),
            w.Clock,
            NullLogger<EligibleApproverRefreshJob>.Instance);
    }

    private static JobRunContext Context() => new(Guid.NewGuid(), "corr-1", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    [Fact(DisplayName = "103 D-78: the job re-resolves a Pending portion of an invoice In Review after its core record's assignment changed")]
    public async Task Job_FollowsAnAssignmentChange()
    {
        var w = new ApprovalWorld();
        await w.Resolver().RefreshInvoiceAsync(w.InvoiceId, CancellationToken.None);
        w.Core["sprk_assignedtointernal"] = new EntityReference("contact", Carol);

        var result = await Job(w).ExecuteAsync(Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(1);
        w.StoredApprovers.Should().BeEquivalentTo([Alice, Carol]);
    }

    [Fact(DisplayName = "103: the job skips a portion whose invoice is no longer In Review, and does nothing when eBilling is off")]
    public async Task Job_SkipsDecidedInvoices_AndRespectsTheFlag()
    {
        var approved = new ApprovalWorld(invoiceStatus: InvoiceStatusCodes.Approved);
        (await Job(approved).ExecuteAsync(Context(), CancellationToken.None)).ProcessedItems.Should().Be(0);
        approved.Writes.Should().Be(0);

        var off = new ApprovalWorld();
        var skipped = await Job(off, enabled: false).ExecuteAsync(Context(), CancellationToken.None);
        skipped.Skipped.Should().BeTrue();
        off.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "103 ADR-036 A1-4: a faulted portion query throws (no progress possible); a faulted unit is counted and the run completes")]
    public async Task Job_ThrowsOnTheQuery_CountsUnitFaults()
    {
        var w = new ApprovalWorld();
        w.FaultsOnce["read:sprk_invoiceapproval"] = new InvalidOperationException("dataverse down");
        var act = () => Job(w).ExecuteAsync(Context(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        w.Faults["read:contact"] = new InvalidOperationException("dataverse down");
        var result = await Job(w).ExecuteAsync(Context(), CancellationToken.None);
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().Contain("1 approvals faulted");
    }
}
