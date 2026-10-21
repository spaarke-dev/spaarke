using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// Builders for the billing evaluation tests (ebilling-r1 task 072): rule versions as <see cref="BillingFactLoader"/> returns them
/// (the rule type as the option value, as the store holds it), loaded facts, requests, and an evaluator whose loader is never called.
/// </summary>
internal static class BillingEvaluationKit
{
    internal static readonly Guid SubmissionId = Guid.Parse("07200000-0000-4000-8000-0000000000b1");
    internal static readonly Guid FirmId = Guid.Parse("07200000-0000-4000-8000-0000000000f1");
    internal static readonly DateOnly RulesFrom = new(2026, 1, 1);

    /// <summary>A valid body for each of the eleven types (the same bodies as the 071 dispatch guard).</summary>
    internal static readonly IReadOnlyDictionary<RuleType, string> Bodies = new Dictionary<RuleType, string>
    {
        [RuleType.ApprovedTimekeeper] = """{"type":"ApprovedTimekeeper"}""",
        [RuleType.ApprovedRate] = """{"type":"ApprovedRate","tolerancePercent":0}""",
        [RuleType.TimeIncrement] = """{"type":"TimeIncrement","incrementHours":0.5}""",
        [RuleType.DailyHoursCap] = """{"type":"DailyHoursCap","maxHoursPerDay":12}""",
        [RuleType.NonBillableActivity] = """{"type":"NonBillableActivity","codes":["A101"]}""",
        [RuleType.ExpenseRule] = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""",
        [RuleType.InvoiceAge] = """{"type":"InvoiceAge","maxAgeDays":90}""",
        [RuleType.CoreRecordWindow] = """{"type":"CoreRecordWindow","graceDays":0}""",
        [RuleType.Duplicate] = """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""",
        [RuleType.RequiredData] = """{"type":"RequiredData","fields":["taskCode"]}""",
        [RuleType.AiObservation] = """{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}""",
        [RuleType.AddedFee] = """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E101","E109"],"capAmount":100}""",
        [RuleType.Discount] = """{"type":"Discount","discountPercent":10}""",
    };

    /// <summary>A deterministic id from a label, so equal inputs build equal candidates.</summary>
    internal static Guid Id(string label)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(label));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>One version of policy <paramref name="code"/>, current and in force from <see cref="RulesFrom"/> unless told otherwise.</summary>
    internal static BillingRuleCandidate Version(
        string code, RuleType type, string? body = null, RuleSetScope scope = RuleSetScope.ClientDefault,
        DateOnly? from = null, DateOnly? to = null, bool current = true, int number = 1, string? template = null, int? lane = null)
        => new()
        {
            PolicyId = Id("policy:" + code),
            PolicyCode = code,
            PolicyVersionId = Id($"version:{code}:{number}"),
            RuleSetId = Id("ruleset:" + scope),
            Scope = scope,
            RuleType = ((int)type).ToString(CultureInfo.InvariantCulture),
            RuleBody = body ?? Bodies[type],
            MessageTemplate = template,
            ShortHeadline = null,
            ClauseReference = $"Clause of {code} v{number}",
            ClauseLocation = $"OCG {code}",
            InForceFrom = from ?? RulesFrom,
            InForceTo = to,
            IsCurrent = current,
            VersionNumber = number,
            Lane = lane,
        };

    internal static BillingLoadedFacts Loaded(params BillingRuleCandidate[] rules) => new()
    {
        Rules = rules,
        InvoiceSecureRoots = Ordinary,
        ReliabilityThreshold = Threshold,
    };

    internal static BillingEvaluationRequest Request(params BillingLineFacts[] lines) => new()
    {
        SubmissionId = SubmissionId,
        FirmId = FirmId,
        TrackingNumber = "INV-100.2",
        FirmInvoiceNumber = "INV-100",
        InvoiceDate = InvoiceDate,
        ReceivedOn = new DateOnly(2026, 4, 2),
        BillingPeriodStart = new DateOnly(2026, 3, 1),
        BillingPeriodEnd = InvoiceDate,
        Currency = "USD",
        HeaderConfidence = Confidence(),
        Lines = lines,
    };

    /// <summary>The evaluator over a loader that fails the test if it is called (the in-memory evaluation reads nothing).</summary>
    internal static InvoiceComplianceEvaluator Evaluator(
        ILogger<InvoiceComplianceEvaluator>? logger = null,
        Func<BillingRuleDefinition, BillingRuleOrigin, BillingFactSet, IReadOnlyList<BillingFinding>>? handlers = null)
        => new(UnusedLoader(), Gate(), logger ?? NullLogger<InvoiceComplianceEvaluator>.Instance, handlers ?? BillingRuleHandlers.Evaluate);

    private static BillingFactLoader UnusedLoader()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var configuration = new ConfigurationBuilder().Build();
        return new BillingFactLoader(
            entities.Object,
            new Mock<IEbillingSettingsReader>(MockBehavior.Strict).Object,
            new SecureChildShareSynchronizer(entities.Object, null!, null!, configuration, NullLogger<SecureChildShareSynchronizer>.Instance),
            configuration,
            new Sprk.Bff.Api.Telemetry.FinanceTelemetry(),
            NullLogger<BillingFactLoader>.Instance);
    }

    /// <summary>
    /// A 150-line invoice on which every one of the eleven sample rules fires (the 071 dispatch guard's "everything wrong", spread):
    /// unlisted timekeepers, rates over the approved rate, 13.25 hours (off the 0.5 increment, over the 12-hour cap), a listed
    /// activity code, no billed task code, block billing, lines before the core record opened and far older than the invoice, an
    /// expense with a non-reimbursable code over the cap, repeated lines, and a prior with the same firm invoice number.
    /// </summary>
    internal static (BillingEvaluationRequest Request, BillingLoadedFacts Loaded) EverythingWrong(int lineCount = 150)
    {
        var lines = Enumerable.Range(0, lineCount).Select(i => i % 10 == 9
                ? Expense(i) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m }
                : Fee(i) with
                {
                    Date = new DateOnly(2026, 3, 2 + (i % 3)),
                    TimekeeperName = "Unlisted Person",
                    Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NotOnRoster),
                    BilledRate = 700m,
                    Hours = 13.25m,
                    Amount = 9275m,
                    Narrative = "Research, call, draft and review",
                    BilledTaskCode = null,
                    Observations =
                    [
                        new Sprk.Bff.Api.Services.Ai.PublicContracts.InvoiceLineObservation
                        {
                            Type = Sprk.Bff.Api.Services.Ai.PublicContracts.InvoiceLineObservationType.BlockBilling,
                            Confidence = 0.95m,
                            QuotedText = "Research, call, draft and review",
                        },
                    ],
                })
            .ToArray();

        var request = Request(lines) with { InvoiceDate = new DateOnly(2026, 8, 1), BillingPeriodEnd = new DateOnly(2026, 7, 31) };
        var rules = Bodies.Keys.Select(type => Version("BILL-" + type, type)).ToArray();
        var loaded = Loaded(rules) with
        {
            CoreRecord = new BillingCoreRecordFacts { OpenDate = new DateOnly(2026, 3, 10), CloseDate = new DateOnly(2026, 12, 31) },
            PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)],
        };
        return (request, loaded);
    }

    /// <summary>Records every log entry with its level and event id.</summary>
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
