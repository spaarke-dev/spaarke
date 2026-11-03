// SdkColumnReadPermissionSource.ColumnsGrantingReadAsync: the field-permission read of the readability proof, through the SDK.
//
// Dataverse refuses a QueryExpression/FetchXML condition on fieldpermission.entityname given a table's logical name, so the
// query asks the server only for the columns, canread = allowed and the profile link, selects entityname, and the table is
// checked in code. A row counts only when its entityname is a string equal to the table (ignoring case); a row whose table is
// missing or of another type never counts. Every page is read, and a fault on any page throws (the proof is then Faulted).
//
// Module-boundary substitute only (IGenericEntityService) per tests/CLAUDE.md.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class SdkColumnReadPermissionSourceTests
{
    private const string Contact = "contact";
    private const string Organization = "sprk_organization";

    // A column of the same logical name on both tables.
    private const string SharedColumn = "sprk_accesspermissiongrant";

    private static readonly Guid User = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>A source whose field-permission read answers <paramref name="pages"/> in order, recording each query.</summary>
    private static (SdkColumnReadPermissionSource Source, List<(int Page, string? Cookie, QueryExpression Query)> Sent)
        SourceAnswering(params Func<EntityCollection?>[] pages)
    {
        var sent = new List<(int, string?, QueryExpression)>();
        var dataverse = new Mock<IGenericEntityService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                sent.Add((q.PageInfo.PageNumber, q.PageInfo.PagingCookie, q));
                return Task.FromResult(pages[sent.Count - 1]()!);
            });
        return (new SdkColumnReadPermissionSource(dataverse.Object), sent);
    }

    private static Func<EntityCollection?> Page(bool more, string? cookie, params Entity[] rows) => () =>
    {
        var page = new EntityCollection(rows.ToList()) { MoreRecords = more, PagingCookie = cookie };
        return page;
    };

    private static Entity Row(object? table, string column)
    {
        var row = new Entity("fieldpermission", Guid.NewGuid()) { ["attributelogicalname"] = column };
        if (table is not null)
        {
            row["entityname"] = table;
        }

        return row;
    }

    private static Entity RowWithoutTable(string column) => Row(null, column);

    // ── the query ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, "systemuserprofiles")]
    [InlineData(true, "teamprofiles")]
    public async Task TheQuery_HasNoConditionOnTheTable_SelectsIt_AndKeepsTheServerSideConditions(bool throughTeam, string link)
    {
        var (source, sent) = SourceAnswering(Page(false, null));

        await source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn, "sprk_other"], throughTeam, CancellationToken.None);

        var query = sent.Single().Query;
        query.EntityName.Should().Be("fieldpermission");
        query.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "entityname",
            "Dataverse refuses a query condition on fieldpermission.entityname given a logical name");
        query.ColumnSet.Columns.Should().Contain("entityname").And.Contain("attributelogicalname");
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "attributelogicalname"
            && c.Operator == ConditionOperator.In && c.Values.Cast<string>().SequenceEqual(new[] { SharedColumn, "sprk_other" }));
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "canread"
            && c.Operator == ConditionOperator.Equal && Equals(c.Values[0], SecuredColumnReadProof.FieldPermissionAllowed));

        var profiles = query.LinkEntities.Single();
        profiles.LinkToEntityName.Should().Be(link);
        var member = throughTeam ? profiles.LinkEntities.Single() : profiles;
        if (throughTeam)
        {
            member.LinkToEntityName.Should().Be("teammembership");
        }

        member.LinkCriteria.Conditions.Should().ContainSingle(c => c.AttributeName == "systemuserid" && Equals(c.Values[0], User));
    }

    // ── must fire: the table is checked in code ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Contact, Organization)]  // a grant on the organization's column never proves the contact's
    [InlineData(Organization, Contact)]  // a grant on the contact's column never proves the organization's
    public async Task ARowOfAnotherTable_ForTheSameColumnName_IsNotCounted(string asked, string rowTable)
    {
        var (source, _) = SourceAnswering(Page(false, null, Row(rowTable, SharedColumn)));

        (await source.ColumnsGrantingReadAsync(User, asked, [SharedColumn], throughTeam: false, CancellationToken.None))
            .Should().BeEmpty();
    }

    public static TheoryData<object?> UnknownTables => new()
    {
        null,                                          // the attribute is missing
        "",
        "   ",
        2,                                             // not a string: an integer
        new OptionSetValue(2),
        new EntityReference("entity", Guid.NewGuid()),
    };

    [Theory]
    [MemberData(nameof(UnknownTables))]
    public async Task ARowWhoseTableIsUnknown_IsNotCounted(object? table)
    {
        var (source, _) = SourceAnswering(Page(false, null, Row(table, SharedColumn)));

        (await source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn], throughTeam: false, CancellationToken.None))
            .Should().BeEmpty("a row whose table is unknown never counts");
    }

    [Fact]
    public async Task ARowWithANullTable_IsNotCounted()
    {
        var row = Row(null, SharedColumn);
        row["entityname"] = null;
        var (source, _) = SourceAnswering(Page(false, null, row));

        (await source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn], throughTeam: true, CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task TheProof_OverTheContactColumn_IsUnproven_WhenOnlyTheOrganizationsColumnIsGranted()
    {
        var dataverse = new Mock<IGenericEntityService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                var rows = new EntityCollection();
                if (q.EntityName == "systemuser")
                {
                    rows.Entities.Add(new Entity("systemuser", User) { ["systemuserid"] = User });
                }
                else if (q.EntityName == "fieldpermission")
                {
                    rows.Entities.Add(Row(Organization, SharedColumn));
                }

                return Task.FromResult(rows);
            });
        var proof = new SecuredColumnReadProof(dataverse.Object, Contact, SharedColumn, NullLogger.Instance);

        (await proof.ProveAsync(CancellationToken.None)).Should().Be(ColumnReadProofOutcome.Unproven);
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Contact)]
    [InlineData("Contact")]
    public async Task ARowOfTheTable_IsCounted(string rowTable)
    {
        var (source, _) = SourceAnswering(Page(false, null,
            Row(rowTable, SharedColumn), Row(Organization, "sprk_other"), RowWithoutTable("sprk_third")));

        (await source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn, "sprk_other", "sprk_third"], false,
                CancellationToken.None))
            .Should().Equal(SharedColumn);
    }

    // ── paging ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AGrantOnALaterPage_IsCounted_AndEachPageIsAskedWithThePreviousCookie()
    {
        var (source, sent) = SourceAnswering(
            Page(true, "cookie-1", Row(Organization, SharedColumn)),
            Page(true, "cookie-2", Row(Contact, "sprk_other")),
            Page(false, null, Row(Contact, SharedColumn)));

        var granted = await source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn, "sprk_other"], false,
            CancellationToken.None);

        granted.Should().BeEquivalentTo([SharedColumn, "sprk_other"]);
        sent.Select(s => (s.Page, s.Cookie)).Should().Equal((1, null), (2, "cookie-1"), (3, "cookie-2"));
        sent[0].Query.PageInfo.Count.Should().Be(SdkColumnReadPermissionSource.FieldPermissionPageSize);
    }

    [Fact]
    public async Task AFaultOnALaterPage_Throws()
    {
        Func<EntityCollection?> faulted = () => throw new InvalidOperationException("simulated page fault");
        var (source, _) = SourceAnswering(Page(true, "cookie-1", Row(Contact, "sprk_other")), faulted);

        await FluentActions.Awaiting(() =>
                source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn, "sprk_other"], false, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>("a fault is never read as no grant");
    }

    [Fact]
    public async Task ANullPage_Throws()
    {
        var (source, _) = SourceAnswering(Page(true, "cookie-1"), () => null);

        await FluentActions.Awaiting(() =>
                source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn], false, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MorePagesThanTheBound_Throws()
    {
        var pages = Enumerable.Range(1, SdkColumnReadPermissionSource.FieldPermissionMaxPages + 1)
            .Select(i => Page(true, $"cookie-{i}"))
            .ToArray();
        var (source, sent) = SourceAnswering(pages);

        await FluentActions.Awaiting(() =>
                source.ColumnsGrantingReadAsync(User, Contact, [SharedColumn], false, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        sent.Should().HaveCount(SdkColumnReadPermissionSource.FieldPermissionMaxPages);
    }
}
