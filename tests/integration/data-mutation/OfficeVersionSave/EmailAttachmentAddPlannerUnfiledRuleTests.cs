using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143: the one definition of an <c>.eml</c> "filed to no record"
/// (<see cref="EmailAttachmentAddPlanner.IsFiledToNoRecord"/>), which lets an attachments add that names no record add under
/// it. Filed to no record = no link in any <see cref="DocumentLinkFields.All"/> column except the two that name the email
/// itself (<c>sprk_relatedcommunication</c>, <c>sprk_email</c>). Anything else, or an unreadable row, is filed: fail closed.
/// </summary>
[Trait("status", "task-143-word-add-in-r1")]
public class EmailAttachmentAddPlannerUnfiledRuleTests
{
    private static readonly Guid EmlId = Guid.Parse("14314314-0143-4143-8143-0000000000e1");

    [Fact]
    public void RowWithNoLinks_IsFiledToNoRecord() =>
        EmailAttachmentAddPlanner.IsFiledToNoRecord(Row()).Should().BeTrue();

    [Fact]
    public void RowLinkedOnlyToItsOwnEmail_IsFiledToNoRecord() =>
        EmailAttachmentAddPlanner.IsFiledToNoRecord(Row(
                ("sprk_relatedcommunication", "sprk_communication"),
                ("sprk_email", "email")))
            .Should().BeTrue("the communication and the email activity are the email's own identity, not a filing");

    [Theory]
    [InlineData("sprk_matter", "sprk_matter")]
    [InlineData("sprk_relatedmatter", "sprk_matter")]
    [InlineData("sprk_project", "sprk_project")]
    [InlineData("sprk_relatedproject", "sprk_project")]
    [InlineData("sprk_invoice", "sprk_invoice")]
    [InlineData("sprk_workassignment", "sprk_workassignment")]
    [InlineData("sprk_relatedagreement", "sprk_agreement")]
    [InlineData("sprk_relatedcontact", "contact")]
    [InlineData("sprk_relatedevent", "sprk_event")]
    [InlineData("sprk_relatedorganization", "sprk_organization")]
    [InlineData("sprk_relatedservicerequest", "sprk_servicerequest")]
    [InlineData("sprk_relatedtodo", "sprk_todo")]
    [InlineData("sprk_relatedvendororg", "sprk_organization")]
    public void RowLinkedToAnyRecord_IsFiled(string column, string entity) =>
        EmailAttachmentAddPlanner.IsFiledToNoRecord(Row((column, entity), ("sprk_relatedcommunication", "sprk_communication")))
            .Should().BeFalse($"a {column} link files the .eml to a record");

    [Fact]
    public void EveryLinkColumnButTheEmailsOwn_IsAFiling()
    {
        // Pins the rule to the link list itself: a column added to DocumentLinkFields.All is a filing unless it is added here.
        var emailIdentity = new[] { "sprk_relatedcommunication", "sprk_email" };
        foreach (var field in DocumentLinkFields.All.Where(f => !emailIdentity.Contains(f.LogicalName)))
        {
            EmailAttachmentAddPlanner.IsFiledToNoRecord(Row((field.LogicalName, field.TargetEntityLogicalName)))
                .Should().BeFalse($"{field.LogicalName} is a filing");
        }
    }

    [Fact]
    public void EmptyLookup_IsNoLink() =>
        EmailAttachmentAddPlanner.IsFiledToNoRecord(new Entity("sprk_document", EmlId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", Guid.Empty),
        })
            .Should().BeTrue();

    [Fact]
    public void UnreadableRow_IsNotFiledToNoRecord() =>
        EmailAttachmentAddPlanner.IsFiledToNoRecord(null).Should().BeFalse("fail closed");

    private static Entity Row(params (string Column, string Entity)[] links)
    {
        var row = new Entity("sprk_document", EmlId);
        foreach (var (column, entity) in links)
            row[column] = new EntityReference(entity, Guid.NewGuid());
        return row;
    }
}
