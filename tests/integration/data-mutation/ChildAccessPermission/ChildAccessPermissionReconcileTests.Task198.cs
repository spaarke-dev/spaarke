using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 198 Part D: when the record a message is filed under BECOMES Restricted, the job that keeps a
/// communication's Access Permission equal to what it inherits (this reconciler, inside <see cref="SecureChildReconciliationJob"/>)
/// also removes the shares of users flagged external on the message and its content rows — the same bar a re-file runs
/// (<see cref="RestrictedFilingShareBar"/>). Through the job exactly as the scheduler runs it.
/// </summary>
public partial class ChildAccessPermissionReconcileTests
{
    private static readonly Guid ExternalUser = Guid.Parse("e0000198-0000-4000-8000-0000000000e2");
    private static readonly Guid InternalUser = Guid.Parse("e0000198-0000-4000-8000-0000000000a2");

    /// <summary>A Standard matter with a message filed under it and one attachment, both shared with an external and an internal user.</summary>
    private static (Guid Matter, Guid Message, Guid Attachment) SharedMessageUnderAStandardMatter(Harness job)
    {
        job.LinkColumns.Users[ExternalUser] = true;
        job.LinkColumns.Users[InternalUser] = false;
        var matter = job.Root("sprk_matter", Standard);
        var message = job.Child("sprk_communication", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        var attachment = Guid.NewGuid();
        job.World.OrdinaryChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", message));
        foreach (var (table, id) in new[] { ("sprk_communication", message), ("sprk_communicationattachment", attachment) })
        {
            job.Shares.Seed(table, id, DataversePrincipalRef.User(ExternalUser), 1);
            job.Shares.Seed(table, id, DataversePrincipalRef.User(InternalUser), 1);
        }

        return (matter, message, attachment);
    }

    private static bool Holds(Harness job, string table, Guid id, Guid user) =>
        job.Shares.MaskOf(table, id, DataversePrincipalRef.User(user)) is > 0;

    [Fact(DisplayName = "198 D must-fire (job): a matter turning Restricted takes the external user's shares off its messages and their content rows")]
    public async Task Run_WhenAMatterTurnsRestricted_TheExternalUsersSharesOnItsMessagesAreRemoved()
    {
        var job = new Harness();
        var (matter, message, attachment) = SharedMessageUnderAStandardMatter(job);
        await job.RunAsync(); // settles under Standard: nothing removed
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeTrue("the matter is not Restricted yet");

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().Contain(("sprk_communication", message, Restricted));
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeFalse("the message now inherits Restricted");
        Holds(job, "sprk_communicationattachment", attachment, ExternalUser).Should().BeFalse("content rows included");
        Holds(job, "sprk_communication", message, InternalUser).Should().BeTrue("an internal user keeps it");
        Holds(job, "sprk_communicationattachment", attachment, InternalUser).Should().BeTrue();
        job.LastResult!.Success.Should().BeTrue();
    }

    [Fact(DisplayName = "198 D must-not-fire (job): a matter that stays Standard leaves every share on its messages")]
    public async Task Run_WhenAMatterStaysStandard_EveryShareOnItsMessagesIsKept()
    {
        var job = new Harness();
        var (matter, message, attachment) = SharedMessageUnderAStandardMatter(job);
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Limited));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().Contain(("sprk_communication", message, Limited));
        foreach (var user in new[] { ExternalUser, InternalUser })
        {
            Holds(job, "sprk_communication", message, user).Should().BeTrue("Limited is not Restricted");
            Holds(job, "sprk_communicationattachment", attachment, user).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 D fail closed (job): a user that cannot be read is left, and the message is carried to the next run")]
    public async Task Run_WhenAShareHoldersFlagCannotBeRead_TheShareIsLeft_AndTheMessageIsCarried()
    {
        var job = new Harness();
        var (matter, message, _) = SharedMessageUnderAStandardMatter(job);
        job.LinkColumns.Users.Remove(ExternalUser); // the read returns no row for them
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        Holds(job, "sprk_communication", message, ExternalUser).Should().BeTrue("unknown never removes");

        // The user becomes readable: the carried message is visited again and the bar completes, without a new change.
        job.LinkColumns.Users[ExternalUser] = true;
        await job.RunAsync();
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeFalse("the carried row is barred on the next run");
    }
}
