using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.FieldMapping;

/// <summary>
/// #2188 (field-mapping-server-write-path-r1 DI-004) — <see cref="MappingSourceCallerMask"/>: an app-only mapping-source
/// read is reduced to the columns the CALLER can read, by one caller read of the same row; null versus not-null is the only
/// test, and every doubt fails closed (UAC-r2 conditions 1, 3 and 5).
/// </summary>
/// <remarks>
/// The caller's Dataverse is <see cref="MappingSourceCallerReadDouble"/> (the documented <c>IDataverseUserClient</c> mock
/// boundary), which answers in the Web API's shape: every column, a lookup only as <c>_col_value</c>, a field-secured column as
/// null.
/// </remarks>
public class MappingSourceCallerMaskTests
{
    private const string Table = "sprk_project";
    private static readonly Guid SourceId = Guid.Parse("4f1c2a3b-0000-4000-8000-000000002188");
    private static readonly Guid ClientId = Guid.Parse("4f1c2a3b-0000-4000-8000-0000000000c1");

    private static Entity AppRead() => new(Table, SourceId)
    {
        ["sprk_clientreference"] = "ACME-REF",
        ["sprk_secretfee"] = new Money(125000m),
        ["sprk_client"] = new EntityReference("account", ClientId),
        ["sprk_stage"] = new OptionSetValue(3),
    };

    private static readonly string[] Mapped = { "sprk_clientreference", "sprk_secretfee", "sprk_client", "sprk_stage" };

    private static Task<Entity?> Mask(MappingSourceCallerReadDouble caller, Entity appRead, IReadOnlyCollection<string>? mapped = null,
        CancellationToken ct = default)
        => MappingSourceCallerMask.ApplyAsync(caller, Table, SourceId, appRead, mapped ?? Mapped, NullLogger.Instance, ct);

    private static MappingSourceCallerReadDouble CallerSeeing(Entity row) => new(id => id == SourceId ? row : null);

    // ── #2253 fix round 3 (K): `$` matched before a final "\n"; `\z` does not ──────────────────────────────────

    [Theory]
    [InlineData("sprk_clientreference\n", null)]
    [InlineData("sprk_clientreference", "sprk_clientreference")]
    public void WebApiPropertyFor_ANameWithATrailingNewline_IsUnreadable(string column, string? expected) =>
        MappingSourceCallerMask.WebApiPropertyFor(column, "ACME-REF", "sprk_projectid").Should().Be(expected);

    // ── must not fire: what the caller can read is kept ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_WhenTheCallerReadsEveryColumn_KeepsEveryValue_IncludingTheLookupAndTheChoiceLabel()
    {
        var appRead = AppRead();
        appRead.FormattedValues["sprk_stage"] = "Discovery";

        var masked = await Mask(CallerSeeing(appRead), appRead);

        masked.Should().NotBeNull();
        masked!.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
        masked.GetAttributeValue<Money>("sprk_secretfee").Value.Should().Be(125000m);
        masked.GetAttributeValue<EntityReference>("sprk_client").Should().BeEquivalentTo(new EntityReference("account", ClientId));
        masked.GetAttributeValue<OptionSetValue>("sprk_stage").Value.Should().Be(3);
        masked.FormattedValues["sprk_stage"].Should().Be("Discovery", "an option set copied into text renders its label");
    }

    [Fact]
    public async Task ApplyAsync_AsksOneCallerReadOfTheSameRow_WithNoSelect()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);

        await Mask(caller, appRead);

        caller.Paths.Should().ContainSingle().Which.Should().Be($"sprk_projects({SourceId:D})",
            "one request shape for the row: nothing in it depends on what the row holds (#2191 review, K3)");
    }

    /// <summary>
    /// #2191 review K3: the request is the same whether the mapped columns hold values or not, so a column the Web API
    /// cannot select can never turn the read into a 400 only while it holds a value.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_TheRequestIsIdentical_WhetherOrNotTheMappedColumnsHoldValues()
    {
        var full = AppRead();
        var withValues = CallerSeeing(full);
        await Mask(withValues, full);

        var empty = new Entity(Table, SourceId);
        var withoutValues = CallerSeeing(empty);
        await Mask(withoutValues, empty);

        withValues.Paths.Should().ContainSingle();
        withoutValues.Paths.Should().ContainSingle().Which.Should().Be(withValues.Paths.Single());
    }

    /// <summary>
    /// #2191 review K3: a mapped column that holds a value but that the caller read does not answer at all (a column the Web
    /// API does not return, such as a virtual name column) is removed like a masked one — the rest of the source is still read.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_AMappedColumnTheCallerReadDoesNotAnswer_IsRemoved_AndTheSourceStaysAvailable()
    {
        var appRead = AppRead();
        appRead["sprk_clientname"] = "Acme Corp";
        var caller = CallerSeeing(appRead);
        caller.Absent.Add("sprk_clientname");

        var masked = await Mask(caller, appRead, Mapped.Append("sprk_clientname").ToArray());

        masked.Should().NotBeNull("one unanswered column never makes the whole source unavailable");
        masked!.Contains("sprk_clientname").Should().BeFalse();
        masked.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
    }

    public static TheoryData<string, object> ReadableScalarKinds() => new()
    {
        { "sprk_isbillable", true },
        { "sprk_opened", new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc) },
        { "sprk_headcount", 7 },
        { "sprk_bignumber", 9_000_000_000L },
        { "sprk_rate", 12.5m },
        { "sprk_ratio", 0.25d },
    };

    /// <summary>
    /// #2191 review K4: yes/no, date, whole number, big integer, decimal and float values the caller reads are kept — each one
    /// the kind table lists. A kind dropped from it would fail closed silently (never copied); this pins every one.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReadableScalarKinds))]
    public async Task ApplyAsync_AReadableScalarOfEachListedKind_IsKept(string column, object value)
    {
        var appRead = new Entity(Table, SourceId) { [column] = value };

        var masked = await Mask(CallerSeeing(appRead), appRead, new[] { column });

        masked!.Contains(column).Should().BeTrue($"a readable {value.GetType().Name} is copied");
        masked[column].Should().Be(value);
    }

    [Fact]
    public async Task ApplyAsync_TheSourcesOwnPrimaryKey_IsKeptWhenTheCallerReadsIt()
    {
        var appRead = new Entity(Table, SourceId) { ["sprk_projectid"] = SourceId };

        var masked = await Mask(CallerSeeing(appRead), appRead, new[] { "sprk_projectid" });

        masked!.GetAttributeValue<Guid>("sprk_projectid").Should().Be(SourceId);
    }

    [Fact]
    public async Task ApplyAsync_WhenEveryMappedColumnIsEmpty_StillMakesTheSameOneCallerRead()
    {
        var empty = new Entity(Table, SourceId);
        var caller = CallerSeeing(empty);

        var masked = await Mask(caller, empty);

        masked.Should().NotBeNull();
        masked!.Attributes.Should().BeEmpty();
        caller.Paths.Should().ContainSingle("an empty column and a masked one cost the same request")
            .Which.Should().Be($"sprk_projects({SourceId:D})");
    }

    // ── must fire: what the caller cannot read is removed, exactly as if it were empty ───────────────────────────

    [Fact]
    public async Task ApplyAsync_AFieldSecuredColumnTheCallerCannotRead_IsRemoved_TheRestKept()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Secured.Add("sprk_secretfee");

        var masked = await Mask(caller, appRead);

        masked!.Contains("sprk_secretfee").Should().BeFalse("Dataverse masked it for the caller, so it is never copied");
        masked.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
    }

    [Fact]
    public async Task ApplyAsync_ALookupTheCallerCannotRead_IsRemoved()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Secured.Add("sprk_client");

        var masked = await Mask(caller, appRead);

        masked!.Contains("sprk_client").Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_AColumnAbsentFromTheCallerResponse_IsRemoved()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Absent.Add("sprk_clientreference");
        caller.Absent.Add("_sprk_client_value");

        var masked = await Mask(caller, appRead);

        masked!.Contains("sprk_clientreference").Should().BeFalse();
        masked.Contains("sprk_client").Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_AMaskedColumn_ComesOutIdenticalToAnEmptyOne()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Secured.Add("sprk_secretfee");
        var masked = await Mask(caller, appRead);

        var emptyRead = AppRead();
        emptyRead.Attributes.Remove("sprk_secretfee");
        var empty = await Mask(CallerSeeing(emptyRead), emptyRead);

        masked!.Attributes.Should().BeEquivalentTo(empty!.Attributes);
        masked.FormattedValues.Should().BeEquivalentTo(empty.FormattedValues);
    }

    public static TheoryData<string, object> UnreadableKinds() => new()
    {
        { "sprk_partylist", new EntityCollection() },
        { "sprk_image", new byte[] { 1, 2, 3 } },
        { "sprk_contract", Guid.Parse("4f1c2a3b-0000-4000-8000-0000000000f1") }, // a GUID that is not the primary key: maybe a file
        { "sprk_aliased", new AliasedValue("x", "y", 1) },
    };

    [Theory]
    [MemberData(nameof(UnreadableKinds))]
    public async Task ApplyAsync_AColumnOfAnUnreadableKind_IsNeverKept_EvenWhenTheCallerAnswersIt(string column, object value)
    {
        var appRead = new Entity(Table, SourceId) { [column] = value };
        var caller = CallerSeeing(appRead);

        var masked = await Mask(caller, appRead, new[] { column });

        masked!.Contains(column).Should().BeFalse("file, image, party list and unknown kinds fail closed");
    }

    [Fact]
    public async Task ApplyAsync_AMultiSelectChoiceTheCallerReads_IsKept()
    {
        var tags = new OptionSetValueCollection { new OptionSetValue(1), new OptionSetValue(2) };
        var appRead = new Entity(Table, SourceId) { ["sprk_tags"] = tags };
        var caller = CallerSeeing(appRead);

        var masked = await Mask(caller, appRead, new[] { "sprk_tags" });

        masked!.GetAttributeValue<OptionSetValueCollection>("sprk_tags").Should().BeSameAs(tags,
            "the Web API answers a multi-select as \"1,2\" or null, so the null test applies to it (review pass 1, K2)");
    }

    [Fact]
    public async Task ApplyAsync_AMultiSelectChoiceTheCallerCannotRead_IsRemoved()
    {
        var appRead = new Entity(Table, SourceId)
        {
            ["sprk_tags"] = new OptionSetValueCollection { new OptionSetValue(1), new OptionSetValue(2) },
        };
        var caller = CallerSeeing(appRead);
        caller.Secured.Add("sprk_tags");

        var masked = await Mask(caller, appRead, new[] { "sprk_tags" });

        masked!.Contains("sprk_tags").Should().BeFalse();
    }

    [Fact(Timeout = 10000)] // without the bound the read never returns: a failure, not a hung run
    public async Task ApplyAsync_WhenTheCallerReadNeverAnswers_IsBounded_AndTheSourceIsUnavailable()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.HangUntilCancelled = true;

        var masked = await MappingSourceCallerMask.ApplyAsync(
            caller, Table, SourceId, appRead, Mapped, NullLogger.Instance, CancellationToken.None, timeout: TimeSpan.FromMilliseconds(50));

        masked.Should().BeNull("a caller read past its bound makes the source unavailable (review pass 1, K4)");
    }

    [Fact]
    public async Task ApplyAsync_AColumnNameThatIsNotAPlainLogicalName_IsNeverKept()
    {
        var appRead = new Entity(Table, SourceId) { ["sprk_x,_a_value"] = "injected" };
        var caller = CallerSeeing(appRead);

        var masked = await Mask(caller, appRead, new[] { "sprk_x,_a_value" });

        masked!.Attributes.Should().BeEmpty();
        caller.Paths.Single().Should().Be($"sprk_projects({SourceId:D})", "nothing from the app read reaches the request");
    }

    [Fact]
    public async Task ApplyAsync_KeepsOnlyMappedColumns()
    {
        var appRead = AppRead();

        var masked = await Mask(CallerSeeing(appRead), appRead, new[] { "sprk_clientreference" });

        masked!.Attributes.Keys.Should().BeEquivalentTo("sprk_clientreference");
    }

    // ── must fire: a caller read that does not answer makes the source unavailable — never a fallback ───────────

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(0)]
    public async Task ApplyAsync_WhenTheCallerReadFails_TheSourceIsUnavailable(int status)
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.FailWithStatus = status;

        (await Mask(caller, appRead)).Should().BeNull("a failed caller read never falls back to the app-read values");
    }

    [Fact]
    public async Task ApplyAsync_WhenTheCallerReadThrows_TheSourceIsUnavailable()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Throw = new HttpRequestException("connection reset");

        (await Mask(caller, appRead)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_WhenTheCallerReadTimesOut_TheSourceIsUnavailable()
    {
        var appRead = AppRead();
        var caller = CallerSeeing(appRead);
        caller.Throw = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        (await Mask(caller, appRead)).Should().BeNull("a timeout is not the caller's cancellation");
    }

    [Fact]
    public async Task ApplyAsync_WhenTheCallersOwnTokenIsCancelled_Rethrows()
    {
        var appRead = AppRead();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => Mask(CallerSeeing(appRead), appRead, ct: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ApplyAsync_WithNoCallerClient_TheSourceIsUnavailable()
    {
        var masked = await MappingSourceCallerMask.ApplyAsync(
            null, Table, SourceId, AppRead(), Mapped, NullLogger.Instance, CancellationToken.None);

        masked.Should().BeNull("no caller context fails closed");
    }

    [Fact]
    public async Task ApplyAsync_ForATableWithNoEntitySetMapping_TheSourceIsUnavailable()
    {
        var appRead = new Entity("sprk_unmapped", SourceId) { ["sprk_x"] = "v" };

        var masked = await MappingSourceCallerMask.ApplyAsync(
            CallerSeeing(appRead), "sprk_unmapped", SourceId, appRead, new[] { "sprk_x" }, NullLogger.Instance, CancellationToken.None);

        masked.Should().BeNull();
    }
}
