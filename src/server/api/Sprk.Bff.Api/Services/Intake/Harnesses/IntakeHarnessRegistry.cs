using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Intake.Harnesses;

/// <summary>
/// Resolves the intake harness for a request (spaarke-intake-pipeline-r1 FR-08). Built from every
/// DI-registered <see cref="IIntakeHarness"/> (pattern: <c>ToolHandlerRegistry</c>,
/// <c>NodeExecutorRegistry</c>), keyed case-insensitively by <see cref="IIntakeHarness.Key"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which harness</b> is Binding data, not code (ADR-039: the Binding table is the only routing
/// surface). <see cref="ResolveAsync"/> resolves consumer type <see cref="ConsumerTypes.IntakeHarness"/>
/// with <c>consumerCode</c> = the request type and a <see cref="RoutingContext"/> carrying the request
/// type and document type, so the row's <c>sprk_matchconditions</c> (<c>requestType</c>,
/// <c>documentType</c>) decide whether it matches. The matched row's <see cref="Binding.ConsumerCode"/>
/// is the harness key.
/// </para>
/// <para>
/// <b>No harness is never an exception</b> (FR-08c, FR-10): a blank or malformed request type, no
/// matching Binding, a key with no registered harness, or a harness that declines the route all
/// return an <see cref="IntakeHarnessResolution"/> without a harness, which the orchestrator maps to
/// manual triage. Duplicate harness keys are a programming error and fail construction.
/// </para>
/// <para>
/// <b>Lifetime</b>: scoped, because <see cref="IConsumerRoutingService"/> and the harnesses are scoped.
/// </para>
/// </remarks>
public sealed class IntakeHarnessRegistry
{
    // A request type is a deterministic token from the request record (ADR-015); anything else is
    // refused before it reaches routing, logs or the routing cache key.
    private static readonly Regex RequestTypeToken = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Dictionary<string, IIntakeHarness> _harnesses;
    private readonly IConsumerRoutingService _routing;
    private readonly ILogger<IntakeHarnessRegistry> _logger;

    public IntakeHarnessRegistry(
        IEnumerable<IIntakeHarness> harnesses,
        IConsumerRoutingService routing,
        ILogger<IntakeHarnessRegistry> logger)
    {
        _routing = routing;
        _logger = logger;
        _harnesses = new Dictionary<string, IIntakeHarness>(StringComparer.OrdinalIgnoreCase);

        foreach (var harness in harnesses)
        {
            var key = harness.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"Intake harness {harness.GetType().FullName} has a blank Key. " +
                    "Each harness needs the key its intake-harness Binding row names in sprk_consumercode.");
            }

            if (!_harnesses.TryAdd(key, harness))
            {
                throw new InvalidOperationException(
                    $"Duplicate intake harness key '{key}': {_harnesses[key].GetType().FullName} and " +
                    $"{harness.GetType().FullName} are both registered under it. Harness keys must be unique " +
                    "(case-insensitive); give one harness a different key and Binding row.");
            }
        }

        _logger.LogInformation(
            "Intake harness registry built: {HarnessCount} harnesses ({HarnessKeys})",
            _harnesses.Count,
            string.Join(",", _harnesses.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Route a request to its harness through the <c>intake-harness</c> Binding (FR-08a). Never throws
    /// for a missing harness; routing failures degrade to <see cref="IntakeHarnessResolutionOutcome.NoBinding"/>
    /// (<see cref="IConsumerRoutingService"/> never throws to its caller).
    /// </summary>
    /// <param name="requestType">Request type token (e.g. <c>nda</c>).</param>
    /// <param name="documentType">Classified document type, or null when not classified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IntakeHarnessResolution> ResolveAsync(
        string? requestType,
        string? documentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestType) || !RequestTypeToken.IsMatch(requestType))
        {
            // The value is not logged: it failed the token check, so it may not be an identifier.
            _logger.LogWarning("Intake harness not resolved: the request type is blank or not a token (outcome={Outcome})",
                IntakeHarnessResolutionOutcome.InvalidRequestType);
            return new IntakeHarnessResolution { Outcome = IntakeHarnessResolutionOutcome.InvalidRequestType };
        }

        var binding = await _routing.ResolveBindingAsync(
            ConsumerTypes.IntakeHarness,
            consumerCode: requestType,
            context: new RoutingContext { RequestType = requestType, DocumentType = documentType },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (binding is null)
        {
            _logger.LogInformation(
                "Intake harness not resolved: no intake-harness Binding for requestType={RequestType} (outcome={Outcome})",
                requestType, IntakeHarnessResolutionOutcome.NoBinding);
            return new IntakeHarnessResolution { Outcome = IntakeHarnessResolutionOutcome.NoBinding };
        }

        var key = string.IsNullOrWhiteSpace(binding.ConsumerCode) ? "default" : binding.ConsumerCode;
        var resolution = Resolve(key, new IntakeHarnessRoute(requestType, documentType)) with { BindingId = binding.BindingId };

        _logger.LogInformation(
            "Intake harness resolution: requestType={RequestType}, bindingId={BindingId}, harnessKey={HarnessKey}, outcome={Outcome}",
            requestType, binding.BindingId, key, resolution.Outcome);

        return resolution;
    }

    /// <summary>
    /// Look a harness up by key (e.g. the key stored on a run being resumed) and apply its
    /// <see cref="IIntakeHarness.CanHandle"/> check. Never throws for an unknown key.
    /// </summary>
    public IntakeHarnessResolution Resolve(string? harnessKey, IntakeHarnessRoute route)
    {
        if (string.IsNullOrWhiteSpace(harnessKey) || !_harnesses.TryGetValue(harnessKey, out var harness))
        {
            return new IntakeHarnessResolution
            {
                Outcome = IntakeHarnessResolutionOutcome.UnregisteredKey,
                HarnessKey = harnessKey,
            };
        }

        if (!harness.CanHandle(route))
        {
            return new IntakeHarnessResolution
            {
                Outcome = IntakeHarnessResolutionOutcome.Declined,
                HarnessKey = harness.Key,
            };
        }

        return new IntakeHarnessResolution
        {
            Outcome = IntakeHarnessResolutionOutcome.Resolved,
            Harness = harness,
            HarnessKey = harness.Key,
        };
    }
}
