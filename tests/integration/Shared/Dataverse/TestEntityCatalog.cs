using System;
using System.Collections.Generic;
using Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The ONE test-side model of <see cref="ISecurableEntityRegistry.ClassifyEntityAsync"/>'s answer, for the
/// registry doubles that stand in for live metadata (unified-access-control-r2 task 151 review).
/// </summary>
/// <remarks>
/// Mirrors the production rule — trim + lower-case, securable wins (securable ⊆ known) unless the owner ruled the entity's
/// flag is not a security input (<see cref="SecurableEntityRegistry.FlagIsNotASecurityInput"/>: <c>sprk_invoice</c>, task
/// 150), otherwise known →
/// <see cref="EntitySecurability.NotSecurable"/>, otherwise <see cref="EntitySecurability.NotAnEntity"/> — so
/// each double states only its WORLD (which entities exist, which are securable), never a private copy of the
/// classification. A double that drifted from this rule would test the resolver against an answer the real
/// registry never gives. The real rule is exercised directly in <c>SecurableEntityRegistryTests</c>.
/// </remarks>
internal static class TestEntityCatalog
{
    /// <param name="name">The name the resolver asked about.</param>
    /// <param name="isSecurable">Whether the (normalized) LOGICAL name carries <c>sprk_issecure</c> in this world.</param>
    /// <param name="isKnown">Whether the (normalized) LOGICAL name is an entity in this world.</param>
    public static EntitySecurability Classify(string? name, Func<string, bool> isSecurable, Func<string, bool> isKnown)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return EntitySecurability.NotAnEntity;
        }

        var normalized = name.Trim().ToLowerInvariant();

        if (isSecurable(normalized) && !SecurableEntityRegistry.FlagIsNotASecurityInput.Contains(normalized))
        {
            return EntitySecurability.Securable;
        }

        return isKnown(normalized) ? EntitySecurability.NotSecurable : EntitySecurability.NotAnEntity;
    }

    /// <summary>Set-based convenience over <see cref="Classify(string?, Func{string, bool}, Func{string, bool})"/>.</summary>
    public static EntitySecurability Classify(string? name, IReadOnlySet<string> securable, IReadOnlySet<string> known)
        => Classify(name, securable.Contains, known.Contains);
}
