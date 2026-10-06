// -----------------------------------------------------------------------------
// NoProducerAttribute.cs
//
// Task 245a (G25 — run-context contract). Marks an InterStepState property that
// no handler in this control plane writes, with the reason. Such a property may
// be read only as an OPTIONAL input (RunContextContractTests rule d) — a handler
// that REQUIRES it can never run.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Declares that no handler writes this <see cref="InterStepState"/> property,
/// and why it still exists.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NoProducerAttribute : Attribute
{
    /// <summary>Declares the property unproduced, for <paramref name="reason"/>.</summary>
    public NoProducerAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why the property exists although nothing writes it.</summary>
    public string Reason { get; }
}
