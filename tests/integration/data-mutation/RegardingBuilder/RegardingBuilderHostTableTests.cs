using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RegardingBuilder;

/// <summary>
/// The shared ADR-024 regarding builder (<see cref="TodoRegardingBuilder"/>) on the two host tables task 141 adds
/// (spaarke-ontology-platform-r1, D-133): <c>sprk_signal</c> and <c>sprk_decisionrecord</c>. Each test writes the
/// association the way that table's writer does and checks the row shape the standard child contract (C1) needs:
/// the subject's own typed lookup (and no other of the table's lookups), and the pair, typed by the
/// <c>sprk_recordtype_ref</c> row when the subject's table has one and untyped (id only) when it has none.
/// </summary>
/// <remarks>
/// <c>sprk_todo</c> is covered, unchanged, by <c>TodoRegardingBuilderTests</c> and <c>ServerWriterAncestorStampingTests</c>.
/// On spaarkedev1 <c>sprk_communication</c> has no catalog row, so it is the untyped case here.
/// </remarks>
public class RegardingBuilderHostTableTests
{
    private static readonly Guid MatterTypeRefId = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df"); // spaarkedev1 catalog row
    private static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid CommunicationId = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc");

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter", true)]
    [InlineData("sprk_communication", "sprk_regardingcommunication", false)]
    public async Task SignalHost_SubjectLookupAndPair_OneSubjectLookup_TypedPairOrUntypedIdOnly_NoStamp(
        string subject, string subjectLookup, bool typed)
    {
        // A resolver that WOULD stamp the matter for a communication: the Signal path must not call it.
        var builder = Builder(CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)));
        var signal = new Entity("sprk_signal");
        var subjectId = subject == "sprk_matter" ? MatterId : CommunicationId;

        await builder.ApplySubjectLookupAndPairAsync(signal, subject, subjectId, "Subject name");

        AssertSubjectAndPair(signal, subject, subjectId, subjectLookup, typed);
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter", true)]
    [InlineData("sprk_communication", "sprk_regardingcommunication", false)]
    public async Task DecisionRecordHost_ApplyResolverFields_OneSubjectLookup_TypedPairOrUntypedIdOnly(
        string subject, string subjectLookup, bool typed)
    {
        var builder = Builder(CoreAncestorResolverFixtures.Inert());
        var record = new Entity("sprk_decisionrecord");
        var subjectId = subject == "sprk_matter" ? MatterId : CommunicationId;

        await builder.ApplyResolverFieldsAsync(record, subject, subjectId, "Subject name");

        AssertSubjectAndPair(record, subject, subjectId, subjectLookup, typed);
    }

    [Fact]
    public async Task TodoHost_SubjectLookupAndPair_IsRefused_ItWouldSkipTheStampAndTheInheritedAccessPermission()
    {
        // Task 141 review K1 (uac-r2): the no-stamp entry point serves only the writers that own their stamp. A To Do written
        // through it would carry no FR-26 stamp and no task-173 inherited Access Permission, which fails open.
        var builder = Builder(CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)));
        var todo = new Entity("sprk_todo");

        var act = async () => await builder.ApplySubjectLookupAndPairAsync(todo, "sprk_matter", MatterId, "Subject name");

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("host");
        todo.Attributes.Should().BeEmpty("nothing is written on a refused host");
    }

    // ── ebilling-r1 task 026 (FR-15, D-94): the invoice-line subject ─────────────────────────────────────────────────

    private static readonly Guid LineTypeRefId = Guid.Parse("fb30d221-bbc9-f111-a05a-7c1e520a989f"); // spaarkedev1, created 2026-10-16
    private static readonly Guid LineId = Guid.Parse("02600000-0000-0000-0000-000000000b01");

    [Theory]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task OntologyHost_InvoiceLineSubject_IsFiledUnderSprkRegardingInvoiceLine_WithTheTypedPair(string table)
    {
        var builder = Builder(CoreAncestorResolverFixtures.Inert());
        var host = new Entity(table);

        await builder.ApplySubjectLookupAndPairAsync(host, "sprk_billingevent", LineId, "Subject name");

        AssertSubjectAndPair(host, "sprk_billingevent", LineId, "sprk_regardinginvoiceline", typed: true, LineTypeRefId);
    }

    [Fact]
    public async Task TodoHost_InvoiceLineSubject_IsStillUnsupported_ItHasNoLineLookup()
    {
        var builder = Builder(CoreAncestorResolverFixtures.Inert());
        var todo = new Entity("sprk_todo");

        var act = async () => await builder.ApplyResolverFieldsAsync(todo, "sprk_billingevent", LineId, "Subject name");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("regardingEntityName");
        todo.Attributes.Should().BeEmpty();
    }

    private static void AssertSubjectAndPair(
        Entity host, string subject, Guid subjectId, string subjectLookup, bool typed, Guid? typeRefId = null)
    {
        var lookups = TodoRegardingBuilder.RegardingLookupsByHostTable[host.LogicalName].Values;
        host.Attributes.Keys.Where(k => lookups.Contains(k)).Should().Equal(new[] { subjectLookup },
            "ADR-024: exactly one subject lookup per row");
        var lookup = host.GetAttributeValue<EntityReference>(subjectLookup);
        lookup.LogicalName.Should().Be(subject);
        lookup.Id.Should().Be(subjectId);

        var cleanId = subjectId.ToString("D").ToLowerInvariant();
        host["sprk_regardingrecordid"].Should().Be(cleanId);
        host["sprk_regardingrecordname"].Should().Be("Subject name");
        host["sprk_regardingrecordurl"].Should().Be($"/main.aspx?pagetype=entityrecord&etn={subject}&id={cleanId}");

        if (typed)
        {
            var type = host["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Subject;
            type.LogicalName.Should().Be("sprk_recordtype_ref");
            type.Id.Should().Be(typeRefId ?? MatterTypeRefId);
        }
        else
        {
            host.Attributes.Should().NotContainKey("sprk_regardingrecordtype",
                "a subject table with no sprk_recordtype_ref row gets the untyped pair (id only), never an error (D-133)");
        }
    }

    private static TodoRegardingBuilder Builder(Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors)
    {
        var catalog = new Mock<ICommunicationDataverseService>(MockBehavior.Strict);
        catalog.Setup(c => c.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterTypeRefId) { ["sprk_recorddisplayname"] = "Matter" });
        catalog.Setup(c => c.QueryRecordTypeRefAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);
        catalog.Setup(c => c.QueryRecordTypeRefAsync("sprk_billingevent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", LineTypeRefId) { ["sprk_recorddisplayname"] = "Invoice Line" });
        return new TodoRegardingBuilder(catalog.Object, coreAncestors, NullLogger<TodoRegardingBuilder>.Instance);
    }
}
