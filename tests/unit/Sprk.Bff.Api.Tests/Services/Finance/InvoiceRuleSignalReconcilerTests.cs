using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 164a fix round K3: the reconcile moved out of <see cref="InvoiceCorrectionReevaluation"/>; the correction context's log lines keep
/// the <c>Field</c> property and the "Correction of {Field}" template they had before the move. The Add line context logs "Add line on invoice ...".
/// </summary>
public sealed class InvoiceRuleSignalReconcilerTests
{
    private static readonly Guid InvoiceId = Guid.Parse("164a0000-0000-4000-8000-0000000000f1");
    private static readonly Guid SubjectId = Guid.Parse("164a0000-0000-4000-8000-0000000000f2");

    private sealed class StateLogger : ILogger
    {
        public readonly List<(string Template, Dictionary<string, object?> Properties)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(p => p.Key, p => p.Value);
            Entries.Add(((string)properties["{OriginalFormat}"]!, properties));
        }
    }

    private static (InvoiceRuleSignalReconciler Reconciler, StateLogger Logger) Reconciler(SignalLogContext context, string? field)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([]));
        var logger = new StateLogger();
        var lifecycle = new SignalEpisodeLifecycle(
            entities.Object,
            (_, _) => throw new InvalidOperationException("no write is expected"),
            (_, _, _, _) => throw new InvalidOperationException("no close is expected"),
            logger);
        var reconciler = new InvoiceRuleSignalReconciler(
            lifecycle, (_, _, _, _, _) => throw new InvalidOperationException("no close is expected"), logger, context, InvoiceId, field);
        return (reconciler, logger);
    }

    private static JsonObject UnplannedRaise() => new()
    {
        ["policyCode"] = "BILL-TEST",
        ["subjectEntity"] = "sprk_billingevent",
        ["subjectId"] = SubjectId.ToString("D"),
        ["raiseFingerprint"] = "fp1",
    };

    public static TheoryData<string, JsonObject> Changes() => new()
    {
        { "no policy or subject", new JsonObject() },
        { "no matching plan entry", UnplannedRaise() },
    };

    [Theory(DisplayName = "164a fix round K3: the correction context's log lines keep the Field property and the 'Correction of {Field}' template")]
    [MemberData(nameof(Changes))]
    public async Task TheCorrectionContext_KeepsTheFieldProperty(string label, JsonObject change)
    {
        _ = label;
        var (reconciler, logger) = Reconciler(SignalLogContext.Correction, "sprk_billedtotal");

        await reconciler.ReconcileRuleAsync(new JsonObject(), change, CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Template.Should().StartWith("Correction of {Field} on invoice {InvoiceId}:");
        entry.Properties.Should().ContainKey("Field").WhoseValue.Should().Be("sprk_billedtotal");
        entry.Properties.Should().NotContainKey("Label");
        entry.Properties["InvoiceId"].Should().Be(InvoiceId);
    }

    [Theory(DisplayName = "164a fix round K3: the Add line context logs 'Add line on invoice {InvoiceId}' with no Field property")]
    [MemberData(nameof(Changes))]
    public async Task TheAddLineContext_LogsItsOwnWording(string label, JsonObject change)
    {
        _ = label;
        var (reconciler, logger) = Reconciler(SignalLogContext.AddLine, field: null);

        await reconciler.ReconcileRuleAsync(new JsonObject(), change, CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Template.Should().StartWith("Add line on invoice {InvoiceId}:");
        entry.Properties.Should().NotContainKey("Field");
        entry.Properties["InvoiceId"].Should().Be(InvoiceId);
    }
}
