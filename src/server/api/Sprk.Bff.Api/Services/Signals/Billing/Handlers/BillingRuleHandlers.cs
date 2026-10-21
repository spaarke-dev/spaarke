namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// A billing rule was refused: it names a type that has no handler, or it lacks a parameter its handler needs. Task 072 catches
/// it as a refused rule: the rule produces no finding, the run continues, and the refusal is logged and metered there.
/// </summary>
public sealed class BillingRuleRefusedException : InvalidOperationException
{
    /// <summary>No handler is wired for the rule's type (an Ontology type, an unknown value, or a billing type added without one).</summary>
    public const string NoHandler = "billing.rule.no_handler";

    /// <summary>The definition lacks a parameter its type requires, or holds one the handler cannot use. The billing gate makes this unreachable; the handlers still refuse it.</summary>
    public const string ParameterMissing = "billing.rule.parameter_missing";

    public BillingRuleRefusedException(string reasonCode, RuleType ruleType, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
        RuleType = ruleType;
    }

    /// <summary><see cref="NoHandler"/> or <see cref="ParameterMissing"/>.</summary>
    public string ReasonCode { get; }

    public RuleType RuleType { get; }
}

/// <summary>
/// The closed dispatch of the eleven billing rule handlers (ebilling-r1 task 071; spec FR-24, D-27): a <c>switch</c> over
/// <see cref="RuleType"/> to a method. No reflection, no registry, no expression evaluation, no data-driven type.
/// </summary>
/// <remarks>
/// <para><b>Pure.</b> A handler judges one rule against one fact set and returns findings: no Dataverse, HTTP, clock, randomness
/// or logging. The evaluation inputs a handler would otherwise read from the world (the reliability threshold, the dates) are in
/// the <see cref="BillingFactSet"/>. The same inputs give the same findings in the same order: invoice-level findings first,
/// then lines in invoice order, and within a line the order the handler checks. Enforced by
/// <c>BillingHandlerPurityGuardTests</c> (ArchTests).</para>
/// <para><b>Fail closed.</b> A rule whose type has no handler is refused with <see cref="BillingRuleRefusedException"/>: never
/// treated as "holds", never skipped without a trace. The Ontology types are refused here as well as at the gate
/// (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>), so a new <see cref="RuleType"/> member is refused until it gets
/// a case below. A test enumerates <see cref="RuleType"/> and fails when a billing member has no handler.</para>
/// <para><b>What a handler does not do.</b> It does not select rules (which rule is in force on a line's date, firm over client
/// default: task 072), assemble facts (072), decide a rejection or check the consequence (task 073), or write a Signal (049). It
/// judges every subject of the invoice; 072 keeps the findings whose subject the rule version is in force for.</para>
/// </remarks>
public static class BillingRuleHandlers
{
    internal delegate IReadOnlyList<BillingFinding> Handler(HandlerContext context);

    /// <summary>Judges <paramref name="rule"/> against <paramref name="facts"/> and returns its findings.</summary>
    /// <exception cref="BillingRuleRefusedException">The rule's type has no handler, or a required parameter is missing.</exception>
    public static IReadOnlyList<BillingFinding> Evaluate(BillingRuleDefinition rule, BillingRuleOrigin origin, BillingFactSet facts)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return EvaluateAs(rule.RuleType, rule, origin, facts);
    }

    /// <summary>Whether <paramref name="ruleType"/> has a handler.</summary>
    public static bool HasHandler(RuleType ruleType) => HandlerFor(ruleType) is not null;

    // Separate from Evaluate so a test can prove the refusal for a type that has no handler: a BillingRuleDefinition only
    // exists for a validated billing type, so the public entry point cannot be given one.
    internal static IReadOnlyList<BillingFinding> EvaluateAs(
        RuleType ruleType, BillingRuleDefinition rule, BillingRuleOrigin origin, BillingFactSet facts)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(facts);

        var handler = HandlerFor(ruleType)
            ?? throw new BillingRuleRefusedException(
                BillingRuleRefusedException.NoHandler,
                ruleType,
                $"No billing handler is wired for rule type {ruleType}; the rule is refused, not skipped.");

        return handler(new HandlerContext(rule, origin, facts));
    }

    /// <summary>The closed switch. Anything that is not one of the thirteen authorable billing members (the eleven of task 071 plus
    /// AddedFee and Discount, task 115) falls to <c>null</c>.</summary>
    internal static Handler? HandlerFor(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => ApprovedTimekeeperHandler.Evaluate,
        RuleType.ApprovedRate => ApprovedRateHandler.Evaluate,
        RuleType.TimeIncrement => TimeIncrementHandler.Evaluate,
        RuleType.DailyHoursCap => DailyHoursCapHandler.Evaluate,
        RuleType.NonBillableActivity => NonBillableActivityHandler.Evaluate,
        RuleType.ExpenseRule => ExpenseRuleHandler.Evaluate,
        RuleType.InvoiceAge => InvoiceAgeHandler.Evaluate,
        RuleType.CoreRecordWindow => CoreRecordWindowHandler.Evaluate,
        RuleType.Duplicate => DuplicateHandler.Evaluate,
        RuleType.RequiredData => RequiredDataHandler.Evaluate,
        RuleType.AiObservation => AiObservationHandler.Evaluate,
        RuleType.AddedFee => AddedFeeHandler.Evaluate,
        RuleType.Discount => DiscountHandler.Evaluate,
        _ => null,
    };

    /// <summary>A required parameter of the rule, or a refusal when the definition lacks it.</summary>
    internal static T Require<T>(T? value, HandlerContext context, string parameter)
        where T : struct =>
        value ?? throw MissingParameter(context, parameter);

    /// <summary>A required list parameter, or a refusal when it is empty.</summary>
    internal static IReadOnlyList<string> RequireList(IReadOnlyList<string> value, HandlerContext context, string parameter) =>
        value.Count > 0 ? value : throw MissingParameter(context, parameter);

    private static BillingRuleRefusedException MissingParameter(HandlerContext context, string parameter) => new(
        BillingRuleRefusedException.ParameterMissing,
        context.Rule.RuleType,
        $"A {context.Rule.RuleType} rule has no '{parameter}' parameter; it is refused, not evaluated.");
}
