using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (FR-23) — a push computes a Copy value with the SAME engine a create does.
/// </summary>
/// <remarks>
/// <para><b>Parity.</b> For each field-type pair a Copy rule is run twice over the same logical source value: through the
/// create-time engine (<see cref="CreateTimeFieldMapping.ApplyFillOnly"/>, typed SDK values, converted for the wire by the
/// create path's own <see cref="RecordCreationService.ToItemValue"/>) and through the push's
/// <see cref="FieldMappingEndpoints.ApplyMappingRule"/> (OData values in, a PATCH payload out). The two must agree. A lookup
/// target is written through its planned <c>@odata.bind</c>, so it is compared by the id it names.</para>
/// <para><b>#1437.</b> A child whose update faults reports a fixed string; the exception text, which can name a table, an id or
/// a column, is only logged, with a correlation id.</para>
/// </remarks>
public class PushCopyParityTests
{
    private static readonly Guid RecordId = Guid.Parse("04000000-0000-4000-8000-000000000001");
    private static readonly Guid ChildId = Guid.Parse("04000000-0000-4000-8000-0000000000c1");
    private static readonly Guid CallerId = Guid.Parse("04000000-0000-4000-8000-0000000000ca");
    private static readonly IReadOnlySet<string> NoProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private const int Text = CreateTimeFieldMapping.FieldTypeText;
    private const int Lookup = CreateTimeFieldMapping.FieldTypeLookup;
    private const int OptionSet = CreateTimeFieldMapping.FieldTypeOptionSet;
    private const int Number = CreateTimeFieldMapping.FieldTypeNumber;
    private const int DateTimeType = CreateTimeFieldMapping.FieldTypeDateTime;
    private const int Boolean = CreateTimeFieldMapping.FieldTypeBoolean;
    private const int Memo = CreateTimeFieldMapping.FieldTypeMemo;

    private static readonly string[] TypeNames = ["Text", "Lookup", "OptionSet", "Number", "DateTime", "Boolean", "Memo"];

    // ───────────────────────── parity: the create engine and the push agree ─────────────────────────

    /// <summary>One logical source value in both domains: the typed value a create reads and the OData value a push reads.</summary>
    public sealed record Case(string Name, int SourceType, int TargetType, object? Typed, string? Label, object? Odata)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<string> CaseNames => new(Cases().Select(c => c.Name));

    private static IEnumerable<Case> Cases()
    {
        yield return new("text-to-text", Text, Text, "Acme Holdings", null, "Acme Holdings");
        yield return new("text-to-memo", Text, Memo, "A long note", null, "A long note");
        yield return new("memo-to-text", Memo, Text, "Line one", null, "Line one");
        yield return new("optionset-to-optionset", OptionSet, OptionSet, new OptionSetValue(100000002), "Active", new SourceChoiceValue(100000002, "Active"));
        yield return new("optionset-to-text", OptionSet, Text, new OptionSetValue(100000002), "Active", new SourceChoiceValue(100000002, "Active"));
        yield return new("date-to-date", DateTimeType, DateTimeType, new DateTime(2027, 3, 4, 5, 6, 7, DateTimeKind.Utc), null, "2027-03-04T05:06:07Z");
        yield return new("date-to-text", DateTimeType, Text, new DateTime(2027, 3, 4, 5, 6, 7, DateTimeKind.Utc), null, "2027-03-04T05:06:07Z");
        yield return new("number-to-number", Number, Number, 42, null, 42L);
        yield return new("money-to-number", Number, Number, new Money(1234.56m), null, 1234.56d);
        yield return new("boolean-to-boolean", Boolean, Boolean, true, null, true);
        yield return new("boolean-to-text", Boolean, Text, true, null, true);
        yield return new("number-to-text", Number, Text, 42, null, 42L);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ACopyRule_GivesTheSameValue_OnACreateAndOnAPush(string name)
    {
        var c = Cases().Single(x => x.Name == name);

        var created = RunCreate(c);
        var pushed = RunPush(c);

        created.Should().NotBeNull($"the create engine copies {c.Name}");
        pushed.Should().ContainKey("sprk_target");
        Normalize(JsonSerializer.SerializeToElement(pushed["sprk_target"])).Should().Be(
            Normalize(created!.Value), $"{c.Name}: a push writes what a create writes");
    }

    [Fact]
    public void ACopiedLookup_NamesTheSameRecord_OnACreateAndOnAPush()
    {
        var contact = Guid.Parse("04000000-0000-4000-8000-0000000000d1");
        var typed = new Entity("sprk_matter") { ["sprk_source"] = new EntityReference("contact", contact) { Name = "Alex" } };
        var createRule = Rule(Lookup, Lookup);
        var target = new Entity("sprk_event");
        CreateTimeFieldMapping.ApplyFillOnly([createRule], target, typed, NoProtected, [], NullLogger.Instance);
        var created = RecordCreationService.ToItemValue(target["sprk_target"], null)!.Value;

        var dto = Dto("Lookup", "Lookup");
        var lookup = new SourceLookupValue(contact, "contact", "Alex");
        var plan = new Dictionary<FieldMappingRuleDto, LookupBindPlan>
        {
            [dto] = new LookupBindPlan("sprk_Target@odata.bind", $"/contacts({contact:D})", null),
        };
        var payload = new Dictionary<string, object?>();

        var result = FieldMappingEndpoints.ApplyMappingRule(dto, new() { ["sprk_source"] = lookup }, payload, plan);

        result.Status.Should().Be(FieldMappingStatus.Mapped);
        created.GetProperty("recordId").GetString().Should().Be(contact.ToString("D"));
        payload["sprk_Target@odata.bind"].Should().Be($"/contacts({contact:D})", "the push binds the record a create names");
    }

    [Fact]
    public void ACopiedLookup_IntoText_WritesTheDisplayName_OnACreateAndOnAPush()
    {
        var contact = Guid.Parse("04000000-0000-4000-8000-0000000000d1");
        var typed = new Entity("sprk_matter") { ["sprk_source"] = new EntityReference("contact", contact) { Name = "Alex Attorney" } };
        var target = new Entity("sprk_event");
        CreateTimeFieldMapping.ApplyFillOnly([Rule(Lookup, Text)], target, typed, NoProtected, [], NullLogger.Instance);

        var payload = new Dictionary<string, object?>();
        var result = FieldMappingEndpoints.ApplyMappingRule(
            Dto("Lookup", "Text"), new() { ["sprk_source"] = new SourceLookupValue(contact, "contact", "Alex Attorney") }, payload);

        result.Status.Should().Be(FieldMappingStatus.Mapped);
        target["sprk_target"].Should().Be("Alex Attorney");
        payload["sprk_target"].Should().Be("Alex Attorney");
    }

    // ───────────────────────── a push applies Copy rules only ─────────────────────────

    [Theory]
    [InlineData("Default")]
    [InlineData("Concat")]
    [InlineData("Template")]
    public void ARuleThatIsNotCopy_IsSkipped_NotAppliedAndNeverAnError(string mappingType)
    {
        var dto = Dto("Text", "Text") with { MappingType = mappingType, DefaultValue = "fallback", Expression = "{sprk_source}" };
        var payload = new Dictionary<string, object?>();

        var result = FieldMappingEndpoints.ApplyMappingRule(dto, new() { ["sprk_source"] = "from the source" }, payload);

        result.Status.Should().Be(FieldMappingStatus.Skipped);
        payload.Should().BeEmpty();
    }

    [Fact]
    public void ACopyRule_DoesNotCarryTheCreateOnlyBehaviours_IntoAPush()
    {
        // The create's fill-only snapshot protects a column the caller supplied; a push is the deliberate re-sync, so a Copy
        // over a value that is already there writes the source's.
        var target = new Entity("sprk_event") { ["sprk_target"] = "already here" };
        var warnings = new List<string>();

        var defaultRule = Rule(Text, Text);
        defaultRule.MappingType = CreateTimeFieldMapping.MappingTypeDefault;
        defaultRule.DefaultValue = "x";

        var notCopy = CreateTimeFieldMapping.ApplyCopyOverwrite(
            [Rule(Text, Text), defaultRule],
            target, new Entity("sprk_matter") { ["sprk_source"] = "from the source" }, NoProtected, warnings, NullLogger.Instance);

        target["sprk_target"].Should().Be("from the source");
        notCopy.Should().Be(1, "the Default rule is counted, not applied");
        warnings.Should().BeEmpty("a rule that is not a Copy rule is never an error");
    }

    // ───────────────────────── #1437: the response never carries the fault ─────────────────────────

    [Fact]
    public async Task AChildWhoseUpdateFaults_ReportsAFixedString_AndLogsTheDetailWithACorrelationId()
    {
        const string detail = "sprk_event 04000000 column sprk_secretcolumn rejected: SQL timeout at dbo.proc_internal";
        var writes = new Mock<IFieldMappingDataverseService>();
        writes.Setup(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(detail));
        var log = new CapturingLogger();

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            writes.Object,
            new StampWorld().Restamper,
            [Dto("Text", "Text")],
            new Dictionary<string, object?> { ["sprk_source"] = "value" },
            "sprk_event",
            [ChildId],
            CallerId,
            log,
            CancellationToken.None);

        (updated, failed).Should().Be((0, 1));
        var error = errors.Should().ContainSingle().Which;
        error.RecordId.Should().Be(ChildId);
        error.Error.Should().NotContain("sprk_secretcolumn").And.NotContain("SQL").And.NotContain("04000000");
        error.Error.Should().Be(FieldMappingEndpoints.ChildUpdateFailedMessage);
        log.Entries.Should().ContainSingle(e => e.Exception != null && e.Exception.Message == detail,
            "the detail goes to the log, where support can find it")
            .Which.Message.Should().Contain("CorrelationId=");
    }

    [Fact]
    public async Task AChildWhoseOwnerCannotBeResolved_ReportsTheStableCodeOnly_NeverTheReason()
    {
        const string reason = "the parent sprk_secretcolumn of 04000000 cannot be read";
        var writes = new Mock<IFieldMappingDataverseService>();
        writes.Setup(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException("sprk_event", "record_owner_parent_unresolved", reason));

        var (_, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            writes.Object, new StampWorld().Restamper, [Dto("Text", "Text")],
            new Dictionary<string, object?> { ["sprk_source"] = "value" }, "sprk_event", [ChildId], CallerId,
            NullLogger.Instance, CancellationToken.None);

        failed.Should().Be(1);
        var message = errors.Should().ContainSingle().Which.Error;
        message.Should().Contain("record_owner_parent_unresolved").And.NotContain("sprk_secretcolumn").And.NotContain("04000000");
    }

    /// <summary>
    /// A rule that really throws inside the engine (the source value cannot be rendered as text): an Error with the fixed text,
    /// never a skip and never the exception's text; the exception and a correlation id are in the log.
    /// </summary>
    [Fact]
    public void ARuleThatFaultsInTheEngine_IsAnError_WithTheFixedText_AndIsLoggedWithACorrelationId()
    {
        var log = new CapturingLogger();
        var payload = new Dictionary<string, object?>();

        var result = FieldMappingEndpoints.ApplyMappingRule(
            Dto("Text", "Text"), new() { ["sprk_source"] = new ThrowsWhenRendered() }, payload, logger: log);

        result.Status.Should().Be(FieldMappingStatus.Error, "an engine fault is a failure, not a skip");
        result.ErrorMessage.Should().Be(FieldMappingEndpoints.RuleFailedMessage);
        payload.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Exception != null && e.Exception.Message == ThrowsWhenRendered.Detail,
            "the engine's fault is logged through the push's logger");
        log.Entries.Should().Contain(e => e.Message.Contains("CorrelationId=") && e.Message.Contains("sprk_target"));
        log.Entries.Should().NotContain(e => e.Message.Contains(ThrowsWhenRendered.Detail) && e.Exception == null,
            "the detail rides on the exception, never in a message a response could copy");
    }

    /// <summary>A rule that throws in the push's own gates (a rule with no source field) goes through the method's catch.</summary>
    [Fact]
    public void ARuleThatThrowsInThePushsOwnGates_IsAnError_WithTheFixedText_AndIsLogged()
    {
        var log = new CapturingLogger();
        var rule = Dto("Text", "Text") with { SourceField = null! };

        var result = FieldMappingEndpoints.ApplyMappingRule(rule, new() { ["sprk_source"] = "x" }, [], logger: log);

        result.Status.Should().Be(FieldMappingStatus.Error);
        result.ErrorMessage.Should().Be(FieldMappingEndpoints.RuleFailedMessage);
        log.Entries.Should().ContainSingle(e => e.Exception is ArgumentNullException)
            .Which.Message.Should().Contain("CorrelationId=");
    }

    /// <summary>The per-child loop hands its logger to each rule: a faulting rule on a push leaves a trace.</summary>
    [Fact]
    public async Task AFaultingRuleOnAPush_IsAnErrorFieldResult_AndIsInThePushsLog()
    {
        var writes = new Mock<IFieldMappingDataverseService>();
        var log = new CapturingLogger();

        var (_, _, _, _, fieldResults) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            writes.Object, new StampWorld().Restamper, [Dto("Text", "Text")],
            new Dictionary<string, object?> { ["sprk_source"] = new ThrowsWhenRendered() }, "sprk_event", [ChildId], CallerId,
            log, CancellationToken.None);

        fieldResults.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Error);
        log.Entries.Should().Contain(e => e.Exception != null && e.Exception.Message == ThrowsWhenRendered.Detail);
    }

    // ───────────────────────── parity: a money target ─────────────────────────

    /// <summary>
    /// A currency column: the create path knows it as money (<c>moneyAttributes</c>) and writes the amount; the push reads the
    /// source amount as the OData number and writes it as is. Both write the same amount.
    /// </summary>
    [Fact]
    public void ACopyIntoAMoneyTarget_GivesTheSameAmount_OnACreateAndOnAPush()
    {
        var source = new Entity("sprk_matter") { ["sprk_source"] = new Money(1234.56m) };
        var target = new Entity("sprk_event");
        CreateTimeFieldMapping.ApplyFillOnly(
            [Rule(Number, Number)], target, source, NoProtected, [], NullLogger.Instance,
            moneyAttributes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_target" });
        var created = RecordCreationService.ToItemValue(target["sprk_target"], null);

        var payload = new Dictionary<string, object?>();
        FieldMappingEndpoints.ApplyMappingRule(Dto("Number", "Number"), new() { ["sprk_source"] = 1234.56d }, payload)
            .Status.Should().Be(FieldMappingStatus.Mapped);

        Normalize(JsonSerializer.SerializeToElement(payload["sprk_target"])).Should().Be(Normalize(created!.Value));
        Normalize(created.Value).Should().Be("1234.56");
    }

    // ───────────────────────── helpers ─────────────────────────

    private static FieldMappingRuleEntity Rule(int sourceType, int targetType) => new()
    {
        Id = Guid.NewGuid(),
        Name = "rule",
        SourceField = "sprk_source",
        SourceFieldType = sourceType,
        TargetField = "sprk_target",
        TargetFieldType = targetType,
        MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = 1,
        IsActive = true,
    };

    private static FieldMappingRuleDto Dto(string sourceType, string targetType) => new()
    {
        Id = Guid.NewGuid(),
        SourceField = "sprk_source",
        SourceFieldType = sourceType,
        TargetField = "sprk_target",
        TargetFieldType = targetType,
        Priority = 1,
        MappingType = "Copy",
    };

    private static JsonElement? RunCreate(Case c)
    {
        var source = new Entity("sprk_matter") { ["sprk_source"] = c.Typed };
        if (c.Label is not null)
            source.FormattedValues["sprk_source"] = c.Label;
        var target = new Entity("sprk_event");
        CreateTimeFieldMapping.ApplyFillOnly([Rule(c.SourceType, c.TargetType)], target, source, NoProtected, [], NullLogger.Instance);
        return target.Attributes.TryGetValue("sprk_target", out var value) ? RecordCreationService.ToItemValue(value, null) : null;
    }

    private static Dictionary<string, object?> RunPush(Case c)
    {
        var payload = new Dictionary<string, object?>();
        var result = FieldMappingEndpoints.ApplyMappingRule(
            Dto(TypeNames[c.SourceType], TypeNames[c.TargetType]), new() { ["sprk_source"] = c.Odata }, payload);
        result.Status.Should().Be(FieldMappingStatus.Mapped, result.ErrorMessage);
        return payload;
    }

    /// <summary>A JSON value in one form for comparison: numbers by value, everything else as written.</summary>
    private static string Normalize(JsonElement element) => element.ValueKind == JsonValueKind.Number
        ? element.GetDecimal().ToString(CultureInfo.InvariantCulture)
        : element.GetRawText();

    /// <summary>A source value whose text form throws: a real fault inside the engine's Copy into a text target.</summary>
    private sealed class ThrowsWhenRendered
    {
        public const string Detail = "sprk_secretcolumn of 04000000 could not be rendered";

        public override string ToString() => throw new InvalidOperationException(Detail);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((formatter(state, exception) + " | " + string.Join(", ", (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(kv => $"{kv.Key}={kv.Value}") ?? []), exception));
    }
}
