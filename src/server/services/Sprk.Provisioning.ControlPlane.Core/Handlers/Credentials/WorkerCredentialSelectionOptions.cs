// -----------------------------------------------------------------------------
// WorkerCredentialSelectionOptions.cs
//
// FR-39 ordered-credential sub-options for the L2 Worker's own Dataverse auth
// (punch row A44.5; customer-provisioning-orchestration-r1 task 205i,
// 2026-08-25). Bound as the `Credentials` property of EnvVarValuesOptions
// (section `EnvVarValues:Credentials`) and SolutionImportOptions (section
// `SolutionImportOptions:Credentials`) — the SAME `{Section}:Credentials:*`
// sub-section convention as the BFF's `Graph:Credentials:*`
// (CredentialSelectionOptions, auth-v4 task 021 / ADR-028 A4, brought in via
// A35). One Bicep param (`requireSecretFreeIdentity` on
// modules/controlplane-worker-app-service.bicep) drives BOTH sections'
// app settings so the two chains cannot drift per-environment (the A38
// fleet-consistency lesson).
//
// MIRROR PROVENANCE (per-member, against the BFF originals):
//   - `Order` starts EMPTY — mirror of CredentialSelectionOptions.Order's
//     binder-merge defence (its class remarks: binding a shorter list over a
//     non-empty default would silently retain surplus defaults). The LEGACY
//     default ([ClientSecret]) is applied in ResolveEffectiveOrder only when
//     the list is entirely empty — never merged under an explicit list.
//   - `RequireSecretFreeIdentity` asserts the ORDER (no secret kind listed),
//     never an observed runtime resolution — mirror of
//     IdentityConfigurationValidator rule 6 (Sprk.Bff.Api/Configuration/
//     IdentityConfigurationValidator.cs:276-295). Unlike the BFF there is NO
//     Development exemption: the Worker's chain config is per-environment and
//     a local run uses the legacy secret order, so the exemption would only
//     mask contradictory config.
//   - Unknown kind names + duplicates fail fast — mirror of
//     CredentialSelectionOptionsValidator (unparseable entry / duplicate
//     rules).
//
// DELIBERATE DIVERGENCE (documented, not accidental): an EMPTY order here
// resolves to the LEGACY [ClientSecret] default instead of failing fast (the
// BFF fails on empty). Reason: A44.5's binding constraint — "Conditional flag
// defaults to preserve current behavior" — every task-142 / task-204a
// environment deployed before this seam existed has NO Credentials section at
// all, and MUST keep booting with identical semantics (empty secret still
// fail-fasts via EnvVarValuesOptions.Validate). The BFF could fail on empty
// because its module applies a canonical default when the section is absent;
// the Worker's absent-section default IS the legacy chain.
//
// §11 justification: see CredentialKind.cs file header (same component
// family, one justification).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Credentials;

/// <summary>
/// Ordered credential list for the L2 Worker's confidential-client Dataverse
/// auth as the shared BFF app registration (H7 env-var writes + H6 solution
/// import). Bound from <c>{Section}:Credentials</c>; mirrors the BFF's
/// <c>Graph:Credentials</c> contract (FR-39 / ADR-028 A4).
///
/// <para><b>Config shape (secret-free environment — §10.2 live contract):</b></para>
/// <code>
/// EnvVarValues__Credentials__Order__0                  = ManagedIdentityFederated
/// EnvVarValues__Credentials__RequireSecretFreeIdentity = true
/// SolutionImportOptions__Credentials__Order__0         = ManagedIdentityFederated
/// SolutionImportOptions__Credentials__RequireSecretFreeIdentity = true
/// </code>
/// (the exact analogue of the BFF's <c>Graph__Credentials__Order__0=ManagedIdentityFederated</c>
/// + <c>Graph__Credentials__RequireSecretFreeIdentity=true</c> pair).
///
/// <para><b>Unconfigured (legacy / prong-3 unmigrated environment):</b> no
/// <c>Credentials</c> section at all — resolves to <c>[ClientSecret]</c> and
/// behaves exactly as task 142 / task 204a shipped (empty
/// <c>EnvVarValues:ClientSecret</c> still fail-fasts Worker boot).</para>
/// </summary>
public sealed class WorkerCredentialSelectionOptions
{
    /// <summary>
    /// The legacy pre-A44.5 chain applied when no order is configured:
    /// client-secret only (task 142 / task 204a behavior, preserved for
    /// prong-3 unmigrated environments per the §6.5 resolution record).
    /// </summary>
    internal static readonly IReadOnlyList<CredentialKind> LegacyDefaultOrder =
        new[] { CredentialKind.ClientSecret };

    /// <summary>
    /// Credential kinds to try, most-preferred first. Names bind
    /// case-insensitively against <see cref="CredentialKind"/>. Starts EMPTY
    /// (see file header — binder-merge defence mirrored from the BFF); empty
    /// resolves to the legacy <c>[ClientSecret]</c> default.
    /// </summary>
    public IList<string> Order { get; set; } = new List<string>();

    /// <summary>
    /// When <c>true</c>, configuration that lists <see cref="CredentialKind.ClientSecret"/>
    /// anywhere in <see cref="Order"/> (or configures no order at all — the
    /// legacy secret default) fails fast at Worker boot. Asserting the ORDER
    /// rather than an observed resolution makes the secret-free property
    /// structural: with no secret kind listed there is nothing beneath MI-FIC
    /// to fall through to, so a broken FIC fails loudly by construction
    /// (mirror of the BFF's <c>Graph:Credentials:RequireSecretFreeIdentity</c> /
    /// IdentityConfigurationValidator rule 6; §10.2 live contract).
    /// </summary>
    public bool RequireSecretFreeIdentity { get; set; }

    /// <summary>
    /// Parses + validates <see cref="Order"/> into the effective credential
    /// chain. Fail-fast (throws <see cref="InvalidOperationException"/>) on:
    /// unknown kind name, duplicate kind, or a
    /// <see cref="RequireSecretFreeIdentity"/> contradiction (secret kind
    /// listed, or no order configured at all). An empty order with
    /// <see cref="RequireSecretFreeIdentity"/> unset resolves to
    /// <see cref="LegacyDefaultOrder"/>. Called from both the boot-time
    /// options validators (EnvVarValuesOptions.Validate /
    /// SolutionImportOptions.Validate) and
    /// <see cref="WorkerDataverseCredentialFactory.Create"/>, so every
    /// consumer enforces one identical contract.
    /// </summary>
    /// <param name="sectionName">Owning configuration section name (for actionable messages), e.g. <c>EnvVarValues</c>.</param>
    internal IReadOnlyList<CredentialKind> ResolveEffectiveOrder(string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        if (Order.Count == 0)
        {
            if (RequireSecretFreeIdentity)
            {
                throw new InvalidOperationException(
                    $"Configuration '{sectionName}:Credentials:RequireSecretFreeIdentity' is true but " +
                    $"'{sectionName}:Credentials:Order' is not configured — the implicit legacy default is " +
                    $"[{nameof(CredentialKind.ClientSecret)}], which contradicts the secret-free assertion. " +
                    $"Set '{sectionName}:Credentials:Order:0' to {nameof(CredentialKind.ManagedIdentityFederated)} " +
                    "(§10.2 live contract: the secret-free chain lists ManagedIdentityFederated as the ONLY entry).");
            }
            return LegacyDefaultOrder;
        }

        var kinds = new List<CredentialKind>(Order.Count);
        for (var index = 0; index < Order.Count; index++)
        {
            var raw = Order[index];
            if (!Enum.TryParse<CredentialKind>(raw?.Trim(), ignoreCase: true, out var kind))
            {
                throw new InvalidOperationException(
                    $"Configuration '{sectionName}:Credentials:Order:{index}' is '{raw}', which is not a known " +
                    $"credential kind. Valid values: {string.Join(", ", Enum.GetNames<CredentialKind>())}. " +
                    "(KeyVaultCertificate is deliberately NOT supported on the L2 Worker — the cert-provisioning " +
                    "estate is unbuilt; see CredentialKind.cs file header.)");
            }
            kinds.Add(kind);
        }

        var duplicates = kinds.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{sectionName}:Credentials:Order' lists {string.Join(", ", duplicates)} more than " +
                "once. A credential that already failed will fail identically on a second attempt, so a repeat " +
                "is always a typo (mirror of the BFF CredentialSelectionOptionsValidator rule).");
        }

        if (RequireSecretFreeIdentity && kinds.Contains(CredentialKind.ClientSecret))
        {
            throw new InvalidOperationException(
                $"Configuration '{sectionName}:Credentials:RequireSecretFreeIdentity' is true and the configured " +
                $"'{sectionName}:Credentials:Order' still lists {nameof(CredentialKind.ClientSecret)}. On a " +
                "secret-free environment the ordered selector must have nothing beneath MI-FIC to fall through " +
                "to (ADR-028 A4 / §10.2 fail-fast; mirror of the BFF IdentityConfigurationValidator rule 6). " +
                "Remove ClientSecret from the order, or — for a deliberate emergency rollback only — set " +
                "RequireSecretFreeIdentity=false for its duration so the deviation is recorded rather than hidden.");
        }

        return kinds;
    }

    /// <summary>
    /// True when the effective chain's PRIMARY credential is the client
    /// secret — i.e. this environment still REQUIRES <c>BFF-API-ClientSecret</c>
    /// (prong-3 unmigrated env, or the unconfigured legacy default). Drives
    /// both the boot-time empty-secret fail-fast
    /// (<c>EnvVarValuesOptions.Validate</c>) and the handlers' runtime
    /// <c>MissingClientSecret</c> guards. Under an MI-FIC-first chain this is
    /// false and an empty secret slot is the SIGNAL (auth-v4 §9.1 — never a
    /// sentinel).
    /// </summary>
    internal bool ClientSecretIsRequiredFirst(string sectionName)
        => ResolveEffectiveOrder(sectionName)[0] == CredentialKind.ClientSecret;
}
