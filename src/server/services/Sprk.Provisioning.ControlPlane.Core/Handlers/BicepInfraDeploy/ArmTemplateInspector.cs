// -----------------------------------------------------------------------------
// ArmTemplateInspector.cs
//
// Task 245b. Structural checks on the ARM template H2a is about to deploy (the
// JSON on <see cref="BicepDeployRequest.Template"/>), before any ARM call:
//
//   R2 (model pinning, ADR-020): every Azure OpenAI model deployment names a
//      pinned version — not `latest`, not blank, not absent (an absent version
//      lets Azure pick and later auto-upgrade the model).
//   R3 (HANDLER-10 / F16): no `keyVaultReferenceIdentity` set to the literal
//      `SystemAssigned` — with a UAMI-only App Service that silently breaks
//      every @Microsoft.KeyVault(...) reference (ADR-028, spec FR-33 T1).
//   R4 (task 246): the template declares every output H2a requires
//      (ArmDeploymentRunner.RequiredOutputNames). A template published before an
//      output was added would otherwise deploy and then quarantine the run.
//
// WHY THE TEMPLATE, NOT THE .bicep SOURCE: its predecessor
// (FileBicepTemplateInspector) read infrastructure/bicep/*.bicep from
// AppContext.BaseDirectory. The L2 publish never contained that tree, so the
// inspector threw on every real run; and even where it existed it described
// whatever source was on that disk, not the CI-compiled artifact H2a actually
// deploys. Inspecting the resolved template checks the bytes that ship.
//
// RETIRED RULE — R1 "no per-customer Redis": written for the pre-D-12 shared
// tier (Q-E FR-12). D-12 (2026-09-28) made Redis per-customer in both models
// and customer.bicep wires modules/redis.bicep unconditionally, so R1 would
// have quarantined every correct deploy. It is not carried forward.
//
// HOW MODEL VERSIONS APPEAR IN COMPILED ARM JSON: openai.bicep's deployment
// list is a module parameter, so `az bicep build` emits it as the nested
// template's `parameters.deployments.defaultValue` — objects carrying `model`
// + `version` literals — while the deployments resource itself reads
// `[parameters('deployments')[copyIndex()].version]`. R2 therefore checks
// (a) every such model-descriptor object, and (b) every
// Microsoft.CognitiveServices/accounts/deployments resource whose
// properties.model.version is a literal or missing. ARM expressions (`[...]`)
// are resolved through (a), not evaluated here.
//
// NO INTERFACE (ADR-010 / ADR-038 B5): the inspector is pure and in-memory, so
// H2a takes the concrete type and its tests drive it with real template JSON
// instead of substituting a stub. (Its file-reading predecessor had a seam
// because it did disk I/O.)
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Applies the R2 (model pin) and R3 (Key Vault reference identity) structural checks to the
/// resolved ARM template JSON (task 245b). Pure — no I/O.
/// </summary>
public sealed class ArmTemplateInspector
{
    private const string ModelDeploymentResourceType = "Microsoft.CognitiveServices/accounts/deployments";

    private readonly ILogger<ArmTemplateInspector> _logger;

    /// <summary>Constructs the inspector.</summary>
    public ArmTemplateInspector(ILogger<ArmTemplateInspector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Inspects <see cref="BicepDeployRequest.Template"/>. Domain outcomes never throw; a malformed
    /// template throws <see cref="JsonException"/> so the handler classifies it per §4C.
    /// </summary>
    public BicepTemplateInspectionResult Inspect(BicepDeployRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Template);

        // Malformed JSON throws JsonException — the handler classifies an inspector exception.
        using var document = JsonDocument.Parse(request.Template.Json);
        var unpinned = FindUnpinnedModelDeployment(document.RootElement);
        var kvRefIdentity = FindSystemAssignedKvRefIdentity(document.RootElement);
        var missingOutputs = FindMissingRequiredOutputs(document.RootElement);

        _logger.LogInformation(
            "ArmTemplateInspector: template={TemplateKey} blob={Blob} version={Version} unpinnedModel={Unpinned} kvRefIdentityInvalid={KvRefInvalid} missingOutputs={MissingOutputs}",
            request.Template.TemplateKey, request.Template.ArmJsonBlobName, request.Template.Version,
            unpinned is not null, kvRefIdentity is not null, missingOutputs.Count);

        return new BicepTemplateInspectionResult(
            HasUnpinnedModelDeployment: unpinned is not null,
            UnpinnedModelReference: unpinned ?? string.Empty,
            HasInvalidKvRefIdentity: kvRefIdentity is not null,
            KvRefIdentityReference: kvRefIdentity ?? string.Empty,
            MissingRequiredOutputs: missingOutputs);
    }

    /// <summary>
    /// R4: the names in <see cref="ArmDeploymentRunner.RequiredOutputNames"/> that the template's top-level
    /// <c>outputs</c> object does not declare (ordinal), in list order. Empty when all are declared.
    /// </summary>
    internal static IReadOnlyList<string> FindMissingRequiredOutputs(JsonElement root)
    {
        var declared = root.ValueKind == JsonValueKind.Object
                       && root.TryGetProperty("outputs", out var outputs)
                       && outputs.ValueKind == JsonValueKind.Object
            ? outputs.EnumerateObject().Select(o => o.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return ArmDeploymentRunner.RequiredOutputNames.Where(name => !declared.Contains(name)).ToList();
    }

    /// <summary>
    /// R2: the first model deployment without a pinned version, as a citation (JSON path + deployment
    /// + observed version), or <c>null</c> when every deployment is pinned.
    /// </summary>
    internal static string? FindUnpinnedModelDeployment(JsonElement root)
    {
        string? found = null;
        Walk(root, "$", (element, path) =>
        {
            if (found is not null || element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // (a) Model descriptor: { "name", "model", "version", "capacity" } — the shape openai.bicep's
            //     `deployments` parameter compiles to. A descriptor with no version, or whose version is an
            //     ARM expression, is not pinned (nothing here can say which version Azure will pick).
            if (element.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String
                && (element.TryGetProperty("version", out _) || element.TryGetProperty("capacity", out _)))
            {
                var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : model.GetString();
                if (!element.TryGetProperty("version", out var descriptorVersion))
                {
                    found = $"{path}: deployment '{name}' has no version";
                }
                else if (!IsPinnedLiteral(descriptorVersion)
                         || (descriptorVersion.ValueKind == JsonValueKind.String && IsExpression(descriptorVersion.GetString())))
                {
                    found = $"{path}: deployment '{name}' version '{Describe(descriptorVersion)}'";
                }
                return;
            }

            // (b) The deployments resource itself: a literal version must be pinned; a missing one is unpinned.
            if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), ModelDeploymentResourceType, StringComparison.OrdinalIgnoreCase))
            {
                var resourceName = element.TryGetProperty("name", out var rn) ? Describe(rn) : "(unnamed)";
                if (!element.TryGetProperty("properties", out var properties)
                    || properties.ValueKind != JsonValueKind.Object
                    || !properties.TryGetProperty("model", out var resourceModel)
                    || resourceModel.ValueKind != JsonValueKind.Object
                    || !resourceModel.TryGetProperty("version", out var resourceVersion))
                {
                    found = $"{path}: deployments resource '{resourceName}' has no properties.model.version " +
                            "(Azure would choose — and auto-upgrade — the model version)";
                    return;
                }
                if (resourceVersion.ValueKind == JsonValueKind.String && IsExpression(resourceVersion.GetString()))
                {
                    return; // Resolved through the descriptor objects (a).
                }
                if (!IsPinnedLiteral(resourceVersion))
                {
                    found = $"{path}: deployments resource '{resourceName}' version '{Describe(resourceVersion)}'";
                }
            }
        });
        return found;
    }

    /// <summary>
    /// R3: the first <c>keyVaultReferenceIdentity</c> set to the literal <c>SystemAssigned</c>, as a
    /// citation (JSON path + value), or <c>null</c> when there is none.
    /// </summary>
    internal static string? FindSystemAssignedKvRefIdentity(JsonElement root)
    {
        string? found = null;
        Walk(root, "$", (element, path) =>
        {
            if (found is not null || element.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "keyVaultReferenceIdentity", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Value.GetString(), "SystemAssigned", StringComparison.OrdinalIgnoreCase))
                {
                    found = $"{path}.{property.Name}: '{property.Value.GetString()}'";
                    return;
                }
            }
        });
        return found;
    }

    private static bool IsPinnedLiteral(JsonElement version)
    {
        if (version.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var value = version.GetString();
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "latest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpression(string? value)
        => value is not null && value.StartsWith('[') && value.EndsWith(']') && !value.StartsWith("[[", StringComparison.Ordinal);

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => "(null)",
        _ => value.GetRawText(),
    };

    private static void Walk(JsonElement element, string path, Action<JsonElement, string> visit)
    {
        visit(element, path);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, $"{path}.{property.Name}", visit);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{index++}]", visit);
                }
                break;
        }
    }
}

/// <summary>
/// Result of <see cref="ArmTemplateInspector.Inspect"/>. Each flag maps to a distinct
/// rejection code on the handler; the handler reports the first failing constraint.
/// </summary>
/// <param name="HasUnpinnedModelDeployment">
/// <c>true</c> when the template contains a model deployment whose version is <c>latest</c>,
/// blank or absent (violates ADR-020).
/// </param>
/// <param name="UnpinnedModelReference">
/// Citation of the unpinned deployment (JSON path + deployment name + observed version) — empty
/// when the flag is <c>false</c>.
/// </param>
/// <param name="HasInvalidKvRefIdentity">
/// HANDLER-10 (Wave 2 pre-dispatch remediation 2026-08-27) — F16 verbatim.
/// <c>true</c> when the template sets <c>keyVaultReferenceIdentity</c> to the literal
/// <c>SystemAssigned</c>. Spaarke's convention (ADR-028 + spec.md FR-33 T1) is a UAMI-scoped
/// kvRefIdentity; SystemAssigned combined with a UAMI-only identity silently breaks every
/// <c>@Microsoft.KeyVault(...)</c> reference at runtime.
/// </param>
/// <param name="KvRefIdentityReference">
/// Citation of the invalid kvRefIdentity (JSON path + observed value). Empty when the flag is
/// <c>false</c>.
/// </param>
/// <param name="MissingRequiredOutputs">
/// Task 246 (R4): required ARM outputs the template does not declare; <c>null</c> or empty when it declares all.
/// </param>
public sealed record BicepTemplateInspectionResult(
    bool HasUnpinnedModelDeployment,
    string UnpinnedModelReference,
    bool HasInvalidKvRefIdentity = false,
    string KvRefIdentityReference = "",
    IReadOnlyList<string>? MissingRequiredOutputs = null);
