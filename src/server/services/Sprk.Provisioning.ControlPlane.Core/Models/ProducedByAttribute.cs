// -----------------------------------------------------------------------------
// ProducedByAttribute.cs
//
// Task 245a (G25 — run-context contract). Names the ONE handler that writes an
// InterStepState property. RunContextContractTests reads it to prove that every
// handler reading a property as a REQUIRED input runs strictly after that
// property's producer in DagAdvancer.HandlerDependencies — the check whose
// absence let ~20 handler inputs ship with no producer at all
// (projects/customer-provisioning-orchestration-r1/notes/run-context-dataflow-gap.md).
//
// Every InterStepState property carries exactly one of [ProducedBy] or
// [NoProducer]; the contract test enforces that too.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Declares the handler (a <c>HandlerIds</c> constant) that writes this
/// <see cref="InterStepState"/> property. Exactly one producer per property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ProducedByAttribute : Attribute
{
    /// <summary>Declares <paramref name="handlerId"/> as the property's only writer.</summary>
    /// <param name="handlerId">A <c>HandlerIds</c> constant, e.g. <c>HandlerIds.H2a</c>.</param>
    public ProducedByAttribute(string handlerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        HandlerId = handlerId;
    }

    /// <summary>The producing handler's id.</summary>
    public string HandlerId { get; }
}
