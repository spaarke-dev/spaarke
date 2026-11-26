using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// SPIKE ONLY (spike/specialist-agents-phase0, never merged). See <see cref="ISpecialistProfileSpikeAi"/> for the contract and the
/// justification. Read-only: it reads the caller's systemuser, the Action row and (inside the binder and runner) the caller's own
/// grounding; it writes nothing (no session coordinates are given to the binder, so no context fingerprint is written).
/// </summary>
public sealed class SpecialistProfileSpikeAi : ISpecialistProfileSpikeAi
{
    private readonly ICallerSystemUserResolver _callers;
    private readonly AnalysisActionService _actions;
    private readonly IContextBinder _binder;
    private readonly IActionRunner _runner;
    private readonly ILogger<SpecialistProfileSpikeAi> _logger;

    public SpecialistProfileSpikeAi(
        ICallerSystemUserResolver callers,
        AnalysisActionService actions,
        IContextBinder binder,
        IActionRunner runner,
        ILogger<SpecialistProfileSpikeAi> logger)
    {
        _callers = callers ?? throw new ArgumentNullException(nameof(callers));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SpikeCallerResult> ResolveCallerAsync(ClaimsPrincipal? caller, CancellationToken cancellationToken)
    {
        var resolution = await _callers.ResolveAsync(caller, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsResolved)
        {
            return SpikeCallerResult.Unresolved(resolution.UnresolvedReason ?? "unresolved");
        }

        // The resolver's contract is a "D" GUID; anything else is treated as unresolved, never passed on.
        return Guid.TryParseExact(resolution.SystemUserId, "D", out var id) && id != Guid.Empty
            ? SpikeCallerResult.Resolved(id.ToString("D"))
            : SpikeCallerResult.Unresolved("malformed-systemuserid");
    }

    /// <inheritdoc />
    public async Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Caller);
        ArgumentNullException.ThrowIfNull(request.Positions);
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            throw new ArgumentException("documentText is required.", nameof(request));
        }

        // Exactly the five declared positions: a missing one would silently review against the generic standard on that topic.
        var expected = SpecialistProfileSpike.PositionInputs;
        if (request.Positions.Count != expected.Count || expected.Any(name => !request.Positions.ContainsKey(name)))
        {
            throw new ArgumentException(
                $"The review needs exactly the positions {string.Join(", ", expected)}; got {string.Join(", ", request.Positions.Keys.OrderBy(k => k, StringComparer.Ordinal))}.",
                nameof(request));
        }

        // The tier by name only ("Reasoning"), never a number: the profile states intent, the resolver maps it to a deployment.
        if (!Enum.GetNames<AiModelTier>().Contains(request.ModelTier, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown model tier '{request.ModelTier}'.", nameof(request));
        }

        var tier = Enum.Parse<AiModelTier>(request.ModelTier);

        var action = await _actions.GetActionByCodeAsync(SpecialistProfileSpike.ActionCode, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The Action '{SpecialistProfileSpike.ActionCode}' was not found in Dataverse.");

        var args = new JsonObject { [SpecialistProfileSpike.DocumentTextInput] = request.DocumentText };
        foreach (var name in expected)
        {
            args[name] = JsonNode.Parse(request.Positions[name].GetRawText());
        }

        var bound = await _binder.BindAsync(
            new ContextBindingRequest
            {
                InputSchemaJson = SpecialistProfileSpike.DeclaredInputSchemaJson,
                Args = JsonSerializer.SerializeToElement(args),
                Caller = request.Caller,
                // No TenantId/SessionId: the fingerprint writer needs a session, and this run has none (nothing is written).
            },
            cancellationToken).ConfigureAwait(false);

        // ADR-043: an undeclared input is dropped silently. Prove every position reached the model input, or refuse the run.
        var boundNames = bound.Operand is { Channel: OperandChannel.Input, Input: { ValueKind: JsonValueKind.Object } input }
            ? input.EnumerateObject().Select(p => p.Name).ToList()
            : [];
        var missing = expected.Prepend(SpecialistProfileSpike.DocumentTextInput).Where(n => !boundNames.Contains(n, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"These inputs did not reach the model input and the review was not run: {string.Join(", ", missing)}.");
        }

        // #1934: the linear path ignores Binding tier overrides, so the profile's tier is set on the Action itself.
        var tiered = action with { ModelTier = tier };
        var review = await _runner.RunAsync(
            tiered,
            bound,
            new LinearRunContext
            {
                ConsumerType = SpecialistProfileSpike.ConsumerType,
                CorrelationId = request.CorrelationId,
                TenantId = TenantResolution.ResolveTenantId(request.Caller),
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Specialist spike review ran: action {ActionCode}, tier {ModelTier} (row {RowTier}), {InputCount} inputs bound, correlation {CorrelationId}.",
            SpecialistProfileSpike.ActionCode, tier, action.ModelTier, boundNames.Count, request.CorrelationId);

        return new SpikeReviewResult(review.Clone(), SpecialistProfileSpike.ActionCode, tier.ToString(), boundNames);
    }
}
