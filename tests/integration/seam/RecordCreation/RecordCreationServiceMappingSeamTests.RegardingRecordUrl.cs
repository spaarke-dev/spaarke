using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 045f on the create (<see cref="RecordCreationService"/>): on a pair table outside the
/// invoice family, a record URL the create sends names the record its regarding pair names — read as the caller (the catalog
/// row of the pair's type), or app-only for the system principal — and is written in canonical form. A URL naming another
/// table or record, or no record, or sent with no pair, is refused through the create's existing pre-write refusal (400 with
/// <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>), and nothing is created; a catalog read that fails creates nothing.
/// A pair with an id and no type (a wizard whose catalog row did not resolve) keeps the URL's own table when its id agrees.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid UrlOtherRecord = Guid.Parse("a045f000-0000-4000-8000-0000000000b1");

    private const string RecordUrlColumn = "sprk_regardingrecordurl";

    private static string CanonicalRecordUrl(string table, Guid id) =>
        RegardingRecordType.BuildRecordUrl(table, id.ToString("D").ToLowerInvariant());

    /// <summary>An event filed under the ordinary matter by its lookup and its whole pair, sending <paramref name="url"/>.</summary>
    private RecordCreationRequest EventWithAWholePair(string? url, bool typed = true)
    {
        var values = new List<KeyValuePair<string, object?>>
        {
            FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
            new(SecureRootInheritance.PairIdColumn, OrdinaryMatter.ToString("D")),
            new(RecordUrlColumn, url),
        };
        if (typed)
            values.Add(PairType(MatterType));
        return TypeOnlyEvent([.. values]);
    }

    private void RefusedAsUrlMismatch(RecordCreationOutcome outcome)
    {
        outcome.Succeeded.Should().BeFalse();
        outcome.Refusal!.Code.Should().Be(RegardingPairWrite.RecordUrlMismatchCode, outcome.Refusal.Detail);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status400BadRequest);
        outcome.Refusal.Detail.Should().NotContain(OrdinaryMatter.ToString("D")).And.NotContain(UrlOtherRecord.ToString("D"));
        _creates.Should().BeEmpty();
    }

    public static TheoryData<string, string> CreateUrlsNamingAnotherRecordOrNone() => new()
    {
        { "another record", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={UrlOtherRecord:D}" },
        { "another table", $"/main.aspx?pagetype=entityrecord&etn=sprk_project&id={OrdinaryMatter:D}" },
        { "no record", "/main.aspx?etn=sprk_matter" },
        { "not a URL", "Acme v. Widget" },
    };

    public static TheoryData<string, string> CreateUrlsOfTheOrdinaryMatter() => new()
    {
        { "relative", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter:D}" },
        { "absolute on another host", $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter:D}" },
        { "upper case", $"http://other.example/main.aspx?etn=SPRK_MATTER&id={OrdinaryMatter.ToString("D").ToUpperInvariant()}" },
        { "braced", $"/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}" },
    };

    // ── Must fire ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "045f: a create whose record URL names another record than its pair, or none, is refused and nothing is created")]
    [MemberData(nameof(CreateUrlsNamingAnotherRecordOrNone))]
    public async Task CreateAsync_AUrlNamingAnotherRecordThanThePair_OrNone_IsRefused_AndNothingIsCreated(string form, string url)
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(EventWithAWholePair(url), AsCaller(), CancellationToken.None);

        RefusedAsUrlMismatch(outcome);
        form.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "045f: a create that sends a record URL with no pair is refused")]
    public async Task CreateAsync_AUrlWithNoPair_IsRefused_AndNothingIsCreated()
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
                new(RecordUrlColumn, CanonicalRecordUrl("sprk_matter", OrdinaryMatter))),
            AsCaller(), CancellationToken.None);

        RefusedAsUrlMismatch(outcome);
    }

    [Fact(DisplayName = "045f: a create whose catalog read for the record URL fails upstream creates nothing")]
    public async Task CreateAsync_TheRecordUrlsCatalogReadFailing_CreatesNothing()
    {
        TheRecordTypes();
        var url = CanonicalRecordUrl("sprk_matter", OrdinaryMatter);
        (await Service().CreateAsync(EventWithAWholePair(url), AsCaller(), CancellationToken.None)).Succeeded.Should().BeTrue();
        var reads = CatalogReads(); // the last of them is the record URL's own
        _creates.Clear();
        _user.Paths.Clear();
        var seen = 0;
        _user.FailOn = path => path.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal) && ++seen == reads
            ? DataverseUserResponse.Fail(503, "ServiceUnavailable", "0x80072321 The service is busy.")
            : null;

        var outcome = await Service().CreateAsync(EventWithAWholePair(url), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "045f: the system principal's create with a record URL of another record is refused, read app-only")]
    public async Task CreateAsync_AsTheSystem_AUrlOfAnotherRecord_IsRefused_AndOfItsOwnIsWritten_InCanonicalForm()
    {
        TheRecordTypes();
        _sourceReader
            .Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { [OwnedChildWrite.RecordTypeLogicalNameColumn] = "sprk_matter" });
        var system = RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid));

        async Task<RecordCreationRequest> SystemEvent(string url)
        {
            var body = new Dictionary<string, object?>
            {
                ["sprk_eventname"] = "Hearing",
                ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = OrdinaryMatter },
                ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = MatterType },
                [SecureRootInheritance.PairIdColumn] = OrdinaryMatter.ToString("D"),
                [RecordUrlColumn] = url,
            };
            var mapped = await DataverseWriteItemMapper.MapAsync(
                _scripted, "sprk_event", JsonSerializer.SerializeToElement(body), CancellationToken.None);
            mapped.Item.Should().NotBeNull(mapped.ValidationError);
            return new RecordCreationRequest("sprk_event", new RecordCreationPayload.Mapped(mapped.Item!)) { CorrelationId = FixedCorrelation };
        }

        RefusedAsUrlMismatch(await Service().CreateAsync(
            await SystemEvent(CanonicalRecordUrl("sprk_matter", UrlOtherRecord)), system, CancellationToken.None));

        var own = await Service().CreateAsync(
            await SystemEvent($"https://other.example/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}"), system, CancellationToken.None);

        own.Succeeded.Should().BeTrue(own.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, RecordUrlColumn).Should().Be(CanonicalRecordUrl("sprk_matter", OrdinaryMatter));
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "045f: a create whose record URL names its pair's record is created with the URL in canonical form")]
    [MemberData(nameof(CreateUrlsOfTheOrdinaryMatter))]
    public async Task CreateAsync_AUrlOfThePairsRecord_IsCreated_WithTheUrlInCanonicalForm(string form, string url)
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(EventWithAWholePair(url), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, RecordUrlColumn).Should().Be(CanonicalRecordUrl("sprk_matter", OrdinaryMatter), form);
    }

    [Fact(DisplayName = "045f: an id-only pair (no type) keeps a record URL of that id, in canonical form, with the URL's own table")]
    public async Task CreateAsync_AnIdOnlyPair_WithAUrlOfThatId_IsCreated_WithTheUrlInCanonicalForm()
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(
            EventWithAWholePair($"https://other.example/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}", typed: false),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, RecordUrlColumn).Should().Be(CanonicalRecordUrl("sprk_matter", OrdinaryMatter));
    }

    [Fact(DisplayName = "045f: an id-only pair with a record URL of another id is refused")]
    public async Task CreateAsync_AnIdOnlyPair_WithAUrlOfAnotherId_IsRefused()
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(
            EventWithAWholePair(CanonicalRecordUrl("sprk_matter", UrlOtherRecord), typed: false), AsCaller(), CancellationToken.None);

        RefusedAsUrlMismatch(outcome);
    }

    [Fact(DisplayName = "045f: a create that clears the record URL is created")]
    public async Task CreateAsync_AUrlClear_IsCreated()
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(EventWithAWholePair(url: null), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, RecordUrlColumn).Should().BeNull();
    }
}
