namespace Spaarke.Dataverse;

/// <summary>
/// The field-security-locked copy of a document's SharePoint Embedded item id — unified-access-control-r2 task 171,
/// owner round 72 item 1 (adversarial finding 4).
/// </summary>
/// <remarks>
/// <para><b>Why a copy.</b> <c>sprk_graphitemid</c> cannot be field-secured: it is part of the alternate key
/// <c>sprk_graphitemid_uk</c> (Dataverse 0x80060896), which the dedup and the Compose upsert rely on. So any user with
/// Write on a row can re-point its item — and since every upload is now BFF-created, the pointer check's "same creator"
/// test no longer tells a legitimate item from another BFF-placed one. <c>sprk_graphitemidbound</c> holds the SAME value,
/// is field-secured with the "Spaarke BFF-Managed Field Writers" profile (only the BFF writes it), and the pointer check
/// (<c>RecordContainerResolver.DocumentPointer</c>) requires the two to be equal.</para>
/// <para><b>The rule for writers.</b> Every BFF code path that sets or changes a document's item id also sets
/// <c>[<see cref="BoundItemIdColumn"/>] = &lt;the same item id&gt;</c> on the SAME write, so the copy can never lag the
/// pointer. The arch guard
/// <c>DocumentPointerBindingGuardTests</c> fails the build when a file writes <c>sprk_graphitemid</c> without it. The
/// copy is NEVER derived anywhere else: a generic write that carries <c>sprk_graphitemid</c> from configuration or a
/// playbook is deliberately NOT bound, so such a re-point is refused rather than laundered.</para>
/// </remarks>
public static class DocumentPointerBinding
{
    /// <summary>The document's SharePoint Embedded item id (part of the alternate key; not field-securable).</summary>
    public const string ItemIdColumn = "sprk_graphitemid";

    /// <summary>The field-secured copy only the BFF writes.</summary>
    public const string BoundItemIdColumn = "sprk_graphitemidbound";

    /// <summary>
    /// Does the row's bound copy agree with the item a caller is about to follow? <see cref="BindingState.Mismatch"/> when
    /// the copy names a different item; <see cref="BindingState.Unbound"/> when the copy is empty (a row not yet
    /// backfilled, or written by something other than the BFF).
    /// </summary>
    public static BindingState Compare(string? boundItemId, string? pointerItemId)
    {
        var bound = boundItemId?.Trim();
        if (string.IsNullOrEmpty(bound))
        {
            return BindingState.Unbound;
        }

        return string.Equals(bound, pointerItemId?.Trim(), StringComparison.Ordinal) ? BindingState.Bound : BindingState.Mismatch;
    }

    /// <summary>The outcome of <see cref="Compare"/>.</summary>
    public enum BindingState
    {
        /// <summary>The copy equals the pointer's item.</summary>
        Bound,

        /// <summary>The copy is empty.</summary>
        Unbound,

        /// <summary>The copy names a different item — the pointer was changed by something other than the BFF.</summary>
        Mismatch,
    }
}
