using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Regression for the ebilling-r1 task 078 review findings F1-2 (roster reuse by name discarded the sheet's period, classification and
/// contact, which broke the annual renewal) and F1-3 (publish approved every Pending timekeeper in one transaction, which Dataverse caps
/// at 1,000 requests).
/// </summary>
/// <remarks>
/// Reuses the <see cref="RateCardImportServiceTests.Harness"/> (Dataverse and SPE substituted at their seams; the workbook parser, the
/// validator and the orchestration are real). Controls: reuse when the sheet matches the stored row, a blank contact cell, a publish at
/// exactly the chunk size.
/// </remarks>
public sealed class RateCardImportRosterRenewalAndPublishChunkingTests
{
    private static readonly Guid FirmId = RateCardImportServiceTests.FirmId;

    private static int Cls(string label)
    {
        TimekeeperClassifications.TryParse(label, out var value).Should().BeTrue();
        return value;
    }

    /// <summary>A stored roster row of the firm (active), as the import's roster read would return it.</summary>
    private static Entity Stored(
        string name, string classification = "Partner", string? from = "2026-01-01", string? to = "2026-12-31",
        int status = RateCardImportService.TimekeeperApproved, Guid? contact = null, Guid? id = null)
    {
        var e = new Entity("sprk_timekeeper", id ?? Guid.NewGuid());
        e["sprk_name"] = name;
        e["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        e["statuscode"] = new OptionSetValue(status);
        e["sprk_classification"] = new OptionSetValue(Cls(classification));
        if (from is not null) e["sprk_effectivefrom"] = DateTime.SpecifyKind(DateTime.Parse(from), DateTimeKind.Utc);
        if (to is not null) e["sprk_effectiveto"] = DateTime.SpecifyKind(DateTime.Parse(to), DateTimeKind.Utc);
        if (contact is { } c) e["sprk_contact"] = new EntityReference("contact", c);
        return e;
    }

    private static List<Entity> CreatedTimekeepers(RateCardImportServiceTests.Harness h) =>
        h.Created.Where(e => e.LogicalName == "sprk_timekeeper").ToList();

    // ═══ F1-2: roster reuse ═════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_ASheetRowThatMatchesTheStoredRosterRow_ReusesIt()
    {
        var h = new RateCardImportServiceTests.Harness();
        var contactId = Guid.NewGuid();
        var contact = new Entity("contact", contactId);
        contact["emailaddress1"] = "jane@firm.com";
        var jane = Stored("Jane  Doe", contact: contactId);
        h.Seed(contact, jane);
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("jane doe", email: "Jane@Firm.com") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        CreatedTimekeepers(h).Should().BeEmpty("the sheet row equals the stored row, so it is reused");
        outcome.Value!.TimekeepersReused.Should().Be(1);
        h.Created.Single(e => e.LogicalName == "sprk_ratecardrate")
            .GetAttributeValue<EntityReference>("sprk_timekeeper").Id.Should().Be(jane.Id);
    }

    [Fact]
    public async Task Import_ABlankContactCellOnTheSheet_DoesNotDifferFromAStoredContact()
    {
        var h = new RateCardImportServiceTests.Harness();
        h.Seed(Stored("Jane Doe", contact: Guid.NewGuid()));
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        CreatedTimekeepers(h).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_TheAnnualRenewal_ASheetPeriodAfterTheStoredRowEnds_CreatesANewPendingRow_AndRatesPointAtIt()
    {
        var h = new RateCardImportServiceTests.Harness();
        var jane2026 = Stored("Jane Doe", to: "2026-12-31");
        h.Seed(jane2026);
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", from: "2027-01-01", to: "2027-12-31") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null, from: "2027-01-01", to: "2027-12-31") });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        outcome.Value!.TimekeepersCreated.Should().Be(1);
        outcome.Value.TimekeepersReused.Should().Be(0);
        var created = CreatedTimekeepers(h).Should().ContainSingle().Subject;
        created.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RateCardImportService.TimekeeperPending);
        created.GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        created.GetAttributeValue<DateTime>("sprk_effectiveto").Should().Be(new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        created.Id.Should().NotBe(jane2026.Id);
        h.Created.Single(e => e.LogicalName == "sprk_ratecardrate")
            .GetAttributeValue<EntityReference>("sprk_timekeeper").Id.Should().Be(created.Id, "the 2027 rate belongs to the 2027 row");
        h.Updated.Should().BeEmpty("the stored 2026 row is left alone");
    }

    [Fact]
    public async Task Import_APromotion_ANewClassificationFromTheDayAfterTheStoredRowEnds_CreatesANewPendingRow()
    {
        var h = new RateCardImportServiceTests.Harness();
        h.Seed(Stored("Jane Doe", classification: "Associate", to: "2026-12-31"));
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", classification: "Partner", from: "2027-01-01", to: null) },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null, from: "2027-01-01", to: null) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        var created = CreatedTimekeepers(h).Should().ContainSingle().Subject;
        created.GetAttributeValue<OptionSetValue>("sprk_classification").Value.Should().Be(Cls("Partner"));
    }

    [Fact]
    public async Task Import_WithSeveralStoredRowsOfOneName_ReusesTheOneWhosePeriodTheSheetMatches()
    {
        var h = new RateCardImportServiceTests.Harness();
        var jane2026 = Stored("Jane Doe", to: "2026-12-31");
        var jane2027 = Stored("Jane Doe", from: "2027-01-01", to: "2027-12-31", status: RateCardImportService.TimekeeperPending);
        h.Seed(jane2026, jane2027);
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", from: "2027-01-01", to: "2027-12-31") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null, from: "2027-01-01", to: "2027-12-31") });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done, string.Join("; ", outcome.Errors.Select(e => e.Message)));
        CreatedTimekeepers(h).Should().BeEmpty();
        h.Created.Single(e => e.LogicalName == "sprk_ratecardrate")
            .GetAttributeValue<EntityReference>("sprk_timekeeper").Id.Should().Be(jane2027.Id);
    }

    [Fact]
    public async Task Import_APeriodOverlappingTwoStoredRowsOfOneName_IsRefused_AsAmbiguous()
    {
        var h = new RateCardImportServiceTests.Harness();
        h.Seed(Stored("Jane Doe", to: "2026-12-31"), Stored("Jane Doe", from: "2026-06-01", to: "2027-12-31"));
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message.Should().Contain("More than one timekeeper named 'Jane Doe'");
        h.CreatedWrites.Should().Be(0);
    }

    [Theory]
    [InlineData("Partner", "2026-01-01", "2027-12-31", "Associate", "2026-01-01", "2026-12-31",
        "Jane Doe is on the roster with Effective To 2026-12-31 and classification Associate; this sheet says Effective To 2027-12-31 and classification Partner")]
    [InlineData("Partner", "2026-03-01", "2026-12-31", "Partner", "2026-01-01", "2026-12-31",
        "Jane Doe is on the roster with Effective From 2026-01-01; this sheet says Effective From 2026-03-01")]
    [InlineData("Partner", "2026-01-01", null, "Partner", "2026-01-01", "2026-12-31",
        "Jane Doe is on the roster with Effective To 2026-12-31; this sheet says Effective To (open-ended)")]
    public async Task Import_ASheetPeriodOverlappingAStoredRowWithDifferentValues_IsRefusedPerRow_NamingTheDifference(
        string sheetClass, string sheetFrom, string? sheetTo, string storedClass, string storedFrom, string storedTo, string expected)
    {
        var h = new RateCardImportServiceTests.Harness();
        h.Seed(Stored("Jane Doe", storedClass, storedFrom, storedTo));
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", sheetClass, sheetFrom, sheetTo) },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null, from: sheetFrom, to: sheetTo) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        var error = outcome.Errors.Should().ContainSingle().Subject;
        error.Sheet.Should().Be(RateCardWorkbook.TimekeepersSheet);
        error.Row.Should().Be(2);
        error.Message.Should().Contain(expected);
        h.CreatedWrites.Should().Be(0, "an invalid row writes nothing");
    }

    [Fact]
    public async Task Import_ADifferentContactOnTheSheet_IsADifference()
    {
        var h = new RateCardImportServiceTests.Harness();
        var sheetContact = new Entity("contact", Guid.NewGuid());
        sheetContact["emailaddress1"] = "new@firm.com";
        h.Seed(sheetContact, Stored("Jane Doe", contact: Guid.NewGuid()));
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", email: "new@firm.com") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null) });

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Message
            .Should().Contain("Jane Doe is on the roster with a different contact; this sheet says contact new@firm.com");
        h.CreatedWrites.Should().Be(0);
    }

    [Fact]
    public async Task EndToEnd_Renewal_ImportThenPublish_ApprovesTheNewRow_AndNotTheOldOne()
    {
        var h = new RateCardImportServiceTests.Harness();
        var jane2026 = Stored("Jane Doe", to: "2026-12-31");
        h.Seed(jane2026);
        var bytes = RateCardImportServiceTests.Workbook(
            new[] { RateCardImportServiceTests.Tk("Jane Doe", from: "2027-01-01", to: "2027-12-31") },
            new[] { RateCardImportServiceTests.Rate("Jane Doe", null, from: "2027-01-01", to: "2027-12-31") });
        var imported = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), bytes, default);
        imported.Kind.Should().Be(RateCardOutcomeKind.Done);
        var newRow = CreatedTimekeepers(h).Single();

        var published = await h.Service().PublishAsync(imported.Value!.RateCardId, default);

        published.Kind.Should().Be(RateCardOutcomeKind.Done);
        h.BulkUpdated.Should().ContainSingle().Which.Updates.Select(u => u.id).Should().Equal(newRow.Id);
    }

    // ═══ K1: a timeout is not a client cancel ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_AnOperationCanceledException_ThatIsNotTheCallersCancellation_CleansUpAndAnswersImportFailed()
    {
        var h = new RateCardImportServiceTests.Harness();
        h.FailCreateWhen = e => e.LogicalName == "sprk_ratecard";
        h.CreateFailure = new OperationCanceledException("a Graph or HTTP timeout; the caller's token is not cancelled");

        var outcome = await h.Service().ImportAsync(RateCardImportServiceTests.Request(), RateCardImportServiceTests.ValidWorkbook(), default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(500);
        outcome.ErrorCode.Should().Be(RateCardImportService.CodeImportFailed);
        h.Rows["sprk_timekeeper"].Should().BeEmpty("what the import created is removed");
        h.FileDeletes.Should().ContainSingle("the uploaded workbook is removed");
    }

    // ═══ F1-3: publish approves in chunks ═══════════════════════════════════════════════════════════════════════

    private static (Guid CardId, List<Guid> Pending) SeedCardWithPending(RateCardImportServiceTests.Harness h, int count)
    {
        var cardId = Guid.NewGuid();
        h.Seed(RateCardImportServiceTests.Card(cardId));
        var pending = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            pending.Add(id);
            h.Seed(
                RateCardImportServiceTests.Tkp(id, RateCardImportService.TimekeeperPending),
                RateCardImportServiceTests.StoredRate(cardId, $"R{i}", id, null, new DateTime(2026, 1, 1), null));
        }

        return (cardId, pending);
    }

    [Theory]
    [InlineData(1000, new[] { 1000 })]
    [InlineData(1001, new[] { 1000, 1 })]
    [InlineData(2500, new[] { 1000, 1000, 500 })]
    public async Task Publish_ApprovesPendingTimekeepersInChunksOfAtMost1000_AndSetsTheCardPublishedLast(int pendingCount, int[] expectedChunks)
    {
        var h = new RateCardImportServiceTests.Harness();
        var (cardId, pending) = SeedCardWithPending(h, pendingCount);

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        outcome.Value!.TimekeepersApproved.Should().Be(pendingCount);
        h.BulkUpdated.Select(b => b.Updates.Count).Should().Equal(expectedChunks);
        h.BulkUpdated.SelectMany(b => b.Updates).Select(u => u.id).Should().BeEquivalentTo(pending, "every Pending timekeeper is approved exactly once");

        var order = h.Entities.Invocations
            .Select(i => i.Method.Name == "BulkUpdateAsync" ? "bulk" : i.Method.Name == "UpdateAsync" ? "card" : null)
            .Where(x => x is not null).ToList();
        order.Should().Equal(Enumerable.Repeat("bulk", expectedChunks.Length).Append("card"), "the card is set Published after every chunk");
    }

    // Each In value becomes a SQL parameter and SQL Server refuses more than ~2,100, so a 2,500-timekeeper card would fail on the read
    // before any approval if the ids went in one condition.
    [Fact]
    public async Task Publish_ReadsTheCardsTimekeepersInBatchesOfAtMost500InValues()
    {
        var h = new RateCardImportServiceTests.Harness();
        var (cardId, pending) = SeedCardWithPending(h, 2500);

        var outcome = await h.Service().PublishAsync(cardId, default);

        outcome.Kind.Should().Be(RateCardOutcomeKind.Done);
        outcome.Value!.TimekeepersApproved.Should().Be(2500);
        var inSizes = h.Entities.Invocations
            .Where(i => i.Method.Name == "RetrieveMultipleAsync")
            .SelectMany(i => i.Arguments.OfType<QueryExpression>())
            .SelectMany(q => q.Criteria.Conditions)
            .Where(c => c.Operator == ConditionOperator.In && c.AttributeName == "sprk_timekeeperid")
            .Select(c => c.Values.Count)
            .ToList();
        inSizes.Should().Equal(500, 500, 500, 500, 500);
    }

    [Fact]
    public async Task Publish_WhenALaterChunkFails_TheCardStaysDraft_AndARetryCompletes()
    {
        var h = new RateCardImportServiceTests.Harness();
        var (cardId, _) = SeedCardWithPending(h, 2500);
        var calls = 0;
        var failSecondOnce = true;
        h.Entities.Setup(e => e.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken _) =>
            {
                calls++;
                if (calls == 2 && failSecondOnce)
                {
                    failSecondOnce = false;
                    throw new InvalidOperationException("simulated Dataverse failure");
                }

                foreach (var (id, fields) in updates)
                {
                    h.Rows["sprk_timekeeper"].Single(r => r.Id == id)["statuscode"] = fields["statuscode"];
                }

                return Task.CompletedTask;
            });

        var first = () => h.Service().PublishAsync(cardId, default);
        await first.Should().ThrowAsync<InvalidOperationException>();
        h.Updated.Should().BeEmpty("the card is not Published while timekeepers are still Pending");

        var retry = await h.Service().PublishAsync(cardId, default);

        retry.Kind.Should().Be(RateCardOutcomeKind.Done);
        retry.Value!.TimekeepersApproved.Should().Be(1500, "the 1,000 approved by the failed run are not approved again");
        h.Updated.Should().ContainSingle().Which.Entity.Should().Be("sprk_ratecard");
        h.Rows["sprk_timekeeper"].Should().OnlyContain(r => r.GetAttributeValue<OptionSetValue>("statuscode").Value == RateCardImportService.TimekeeperApproved);
    }
}
