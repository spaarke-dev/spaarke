using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using World = Sprk.Bff.Api.Tests.Services.Finance.InvoiceReviewReadServiceTests.World;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// Invoice Review payload contract (owner review 2026-11-25): every code on a line carries its UTBMS description (<c>description</c> for the AI's code,
/// <c>firmDescription</c> for the firm's), read from the code set the line RECORDED when it was coded, each distinct set loaded once per request. The
/// review is the same service the review route calls; the Dataverse seam is the resolver's own reads, answered from fixture rows.
/// </summary>
public sealed class InvoiceReviewCodeDescriptionsContractTests
{
    private static readonly Guid SubmissionId = Guid.Parse("10000000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
    private static readonly InvoiceContextService.InvoiceCaller Caller = new("caller-oid", "caller-token");
    private static readonly Guid SetA = Guid.Parse("20000000-0000-4000-8000-00000000000a");
    private static readonly Guid SetB = Guid.Parse("20000000-0000-4000-8000-00000000000b");

    /// <summary>The code-set reads of the review: the sets that exist (with their codes), the ones that fault, and every set read that happened.</summary>
    private sealed class CodeCatalog
    {
        public readonly Dictionary<Guid, List<Entity>> Sets = [];
        public readonly HashSet<Guid> Faulted = [];
        public readonly List<Guid> SetReads = [];

        public void Attach(World world)
        {
            world.CodeSetEntities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    var setId = (Guid)query.Criteria.Conditions
                        .Single(c => c.AttributeName is "sprk_billingcodesetid" or "sprk_codeset").Values[0];
                    if (query.EntityName == "sprk_billingcodeset")
                    {
                        SetReads.Add(setId);
                        if (Faulted.Contains(setId))
                        {
                            throw new InvalidOperationException("dataverse down");
                        }

                        return new EntityCollection(Sets.ContainsKey(setId) ? [new Entity("sprk_billingcodeset", setId) { ["sprk_name"] = "UTBMS" }] : []);
                    }

                    return new EntityCollection(Sets.TryGetValue(setId, out var codes) ? codes : []);
                });
        }
    }

    private static Entity Code(BillingCodeKind kind, string code, string? description) => new("sprk_billingcode", Guid.NewGuid())
    {
        ["sprk_code"] = code,
        ["sprk_codetype"] = new OptionSetValue((int)kind),
        ["sprk_description"] = description,
    };

    private static List<Entity> UtbmsCodes() =>
    [
        Code(BillingCodeKind.Task, "L190", "Pleadings"),
        Code(BillingCodeKind.Task, "L110", "Fact Investigation / Development"),
        Code(BillingCodeKind.Task, "L120", null),
        Code(BillingCodeKind.Activity, "A101", "Plan and prepare for"),
        Code(BillingCodeKind.Expense, "E101", "Copying"),
    ];

    private static string Extraction(Guid? setId, string source, string? task, string? firmTask, string? activity = null, string? expense = null)
    {
        var coding = new JsonObject
        {
            ["status"] = "Coded",
            ["task"] = new JsonObject { ["code"] = task, ["firmCode"] = firmTask, ["mismatch"] = task != firmTask },
            ["activity"] = new JsonObject { ["code"] = activity, ["firmCode"] = null, ["mismatch"] = false },
            ["expense"] = new JsonObject { ["code"] = expense, ["firmCode"] = null, ["mismatch"] = false },
        };
        if (setId is not null)
        {
            coding["codeSetId"] = setId.Value.ToString("D");
            coding["codeSetSource"] = source;
        }

        return new JsonObject { ["coding"] = coding }.ToJsonString();
    }

    private static async Task<ReviewCodesDto> ReadCodesAsync(World world, int line = 0)
    {
        var review = await world.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);
        review.Should().NotBeNull();
        return review!.Lines[line].Codes;
    }

    [Fact(DisplayName = "GetReview_LineCodedWithRecordedSet_CarriesDescriptionsOfAiCodeAndFirmCode")]
    public async Task GetReview_LineCodedWithRecordedSet_CarriesDescriptionsOfAiCodeAndFirmCode()
    {
        var catalog = new CodeCatalog();
        catalog.Sets[SetA] = UtbmsCodes();
        var world = new World();
        catalog.Attach(world);
        world.AddLine(1, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L190", "l110", "A101", "E101"));

        var codes = await ReadCodesAsync(world);

        codes.Task.Code.Should().Be("L190");
        codes.Task.Description.Should().Be("Pleadings");
        codes.Task.FirmCode.Should().Be("l110");
        codes.Task.FirmDescription.Should().Be("Fact Investigation / Development", "the lookup ignores case");
        codes.Activity.Description.Should().Be("Plan and prepare for");
        codes.Expense.Description.Should().Be("Copying");

        // It reaches the wire under the names the page reads.
        var json = JsonSerializer.SerializeToElement(codes, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("task");
        json.GetProperty("description").GetString().Should().Be("Pleadings");
        json.GetProperty("firmDescription").GetString().Should().Be("Fact Investigation / Development");
    }

    [Fact(DisplayName = "GetReview_CodeOutsideTheRecordedSetOrWithEmptyDescription_HasNoDescription")]
    public async Task GetReview_CodeOutsideTheRecordedSetOrWithEmptyDescription_HasNoDescription()
    {
        var catalog = new CodeCatalog();
        catalog.Sets[SetA] = UtbmsCodes();
        var world = new World();
        catalog.Attach(world);
        world.AddLine(1, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L999", "L120"));

        var codes = await ReadCodesAsync(world);

        codes.Task.Code.Should().Be("L999");
        codes.Task.Description.Should().BeNull("L999 is not in the set");
        codes.Task.FirmDescription.Should().BeNull("L120 is in the set but has no description");
    }

    [Fact(DisplayName = "GetReview_RecordedSetMissingOrUnreadable_StillReturnsTheReviewWithNullDescriptions")]
    public async Task GetReview_RecordedSetMissingOrUnreadable_StillReturnsTheReviewWithNullDescriptions()
    {
        var catalog = new CodeCatalog();
        catalog.Sets[SetA] = UtbmsCodes();
        catalog.Faulted.Add(SetA);
        var world = new World();
        catalog.Attach(world);
        world.AddLine(1, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L190", "L110"));
        world.AddLine(2, InvoiceLineStatus.Flagged, extraction: Extraction(SetB, "AiSuggestion", "L190", "L110")); // SetB does not exist (inactive or deleted)

        var review = await world.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review.Should().NotBeNull("a code list that cannot be read must never fail the review");
        review!.Lines.Should().HaveCount(2);
        review.Lines.Select(l => l.Codes.Task).Should().OnlyContain(c => c.Code == "L190" && c.Description == null && c.FirmDescription == null);
    }

    [Fact(DisplayName = "GetReview_SeveralLinesAndNoRecordedSet_LoadsEachDistinctSetOnceAndNoneForUncodedLines")]
    public async Task GetReview_SeveralLinesAndNoRecordedSet_LoadsEachDistinctSetOnceAndNoneForUncodedLines()
    {
        var catalog = new CodeCatalog();
        catalog.Sets[SetA] = UtbmsCodes();
        catalog.Sets[SetB] = [Code(BillingCodeKind.Task, "L190", "Pleadings (set B)"), Code(BillingCodeKind.Activity, "A101", "x"), Code(BillingCodeKind.Expense, "E101", "y")];
        var world = new World();
        catalog.Attach(world);
        world.AddLine(1, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L190", "L110"));
        world.AddLine(2, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L110", "L110"));
        world.AddLine(3, InvoiceLineStatus.Flagged, extraction: Extraction(SetA, "MatterType", "L190", "L190"));
        world.AddLine(4, InvoiceLineStatus.Flagged, extraction: Extraction(SetB, "SettingsDefault", "L190", "L190"));
        world.AddLine(5, InvoiceLineStatus.Flagged, extraction: Extraction(null, "", "L190", "L190")); // coded without a recorded set
        world.AddLine(6, InvoiceLineStatus.Flagged); // no extraction at all

        var review = await world.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        catalog.SetReads.Should().BeEquivalentTo([SetA, SetB], "one load per distinct set, not per line");
        review!.Lines.Select(l => l.Codes.Task.Description).Should().Equal(
            "Pleadings", "Fact Investigation / Development", "Pleadings", "Pleadings (set B)", null, null);
    }
}
