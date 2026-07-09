using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// <b>ADR-043 §3 — the ONE disposition-capability source of truth.</b> Disposition capability
/// used to be triplicated across three hand-maintained lists — the dispatch admit-gate
/// (<c>SessionDispatchOrchestrator</c>), the <see cref="OutputRouter"/> routing switch, and
/// <c>ToLedgerValue</c> — which drifted (the compose routing promotion updated two and left the
/// admit-gate un-widened → a live 422). This registry collapses them to one table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Admission derives from routability</b> (ADR-043 §3): the dispatch admit-gate admits EXACTLY
/// the dispositions <see cref="OutputRouter"/> can route — <see cref="IsAdmissible"/> IS
/// <see cref="IsRoutable"/>. The admit-gate calls <see cref="IsAdmissible"/>, the router derives its
/// loud non-routable rejection from <see cref="IsRoutable"/> + <see cref="Entry.NotRoutableReason"/>,
/// and the ledger vocabulary comes from <see cref="ToLedgerValue(BindingDisposition)"/>. The three
/// consumers read ONE table, so they cannot disagree — drift is structurally impossible.
/// </para>
/// <para>
/// <b>Routable vs. ledger-mapped are two axes.</b> Every disposition has a
/// <see cref="Entry.LedgerValue"/> — even the not-yet-routable ones — because the ADR-040 ledger
/// write STORES the entry (with its disposition value) BEFORE any routing leg runs or throws. A
/// not-yet-routable disposition is therefore stored-then-loudly-rejected, never silently dropped.
/// </para>
/// <para>
/// <b>What is NOT in the table</b>: the actual side-effect leg CODE (email delivery, work-product
/// persistence) lives in <see cref="OutputRouter"/> — it cannot be data. The registry guards that
/// binding two ways: (1) the router throws a loud "registry/router drift" error if a
/// <see cref="Entry.Routable"/>=true disposition reaches its switch <c>default</c> with no leg; and
/// (2) the <c>tests/integration/seam/**</c> vertical-slice tests assert admit ⇔ route ⇔ store for
/// each disposition. Adding or realizing a disposition is ONE change HERE.
/// </para>
/// <para>
/// <b>Email leg note (r2)</b>: email is routable (the <see cref="IEmailDispositionSender"/> leg
/// shipped at r1 FR-P3-04), therefore admissible. E-20 realizes NO new send capability and does not
/// alter the existing leg — the "no auto-send in r2" directive is a scope guard against BUILDING
/// auto-send, honored here by leaving the leg untouched. See the E-20 report escalation note.
/// </para>
/// </remarks>
public static class DispositionRoutability
{
    /// <summary>One registry row: a disposition's ledger vocabulary + whether the router can route it.</summary>
    public sealed record Entry
    {
        /// <summary>The <see cref="BindingDisposition"/> this row describes.</summary>
        public required BindingDisposition Disposition { get; init; }

        /// <summary>
        /// The ledger wire vocabulary member written to <c>SessionOutput.Disposition</c>
        /// (<c>informational | work_product | overlay | email | record | notification | compose</c>
        /// — canonical §6.2 / ADR-040). Present for EVERY disposition (store precedes routing).
        /// </summary>
        public required string LedgerValue { get; init; }

        /// <summary>
        /// Whether <see cref="OutputRouter"/> has a REAL routing leg for this disposition (not a loud
        /// stub). Admission on the dispatch path derives from this (ADR-043 §3).
        /// </summary>
        public required bool Routable { get; init; }

        /// <summary>
        /// For a NOT-routable disposition (<see cref="Routable"/>=false), a one-line rationale naming
        /// the missing side-effect leg — surfaced in both the admit-gate rejection and the router's
        /// loud post-store stub. Null for routable dispositions.
        /// </summary>
        public string? NotRoutableReason { get; init; }
    }

    // ── THE registry. Adding/realizing a disposition is ONE edit here. ────────────────────────────
    private static readonly IReadOnlyDictionary<BindingDisposition, Entry> Registry = new[]
    {
        // Realized legs (routable ⇒ admissible on the dispatch path).
        new Entry { Disposition = BindingDisposition.Informational, LedgerValue = "informational", Routable = true },
        new Entry { Disposition = BindingDisposition.WorkProduct,   LedgerValue = "work_product",  Routable = true },
        new Entry { Disposition = BindingDisposition.Email,         LedgerValue = "email",         Routable = true },
        new Entry { Disposition = BindingDisposition.Compose,       LedgerValue = ComposeDisposition.DispositionValue, Routable = true },

        // Not-yet-routable legs (routable=false ⇒ LOUD rejection, never a silent drop). Realizing any
        // of these requires a NEW side-effect mechanism (out of E-20 scope per the ADR-043 escalation
        // rule — single-source the registry + register loud rather than invent an unscoped side effect).
        new Entry
        {
            Disposition = BindingDisposition.Overlay, LedgerValue = "overlay", Routable = false,
            NotRoutableReason = "the bidirectional widget-overlay annotation side-effect leg is not yet built — lands in a later wave",
        },
        new Entry
        {
            Disposition = BindingDisposition.Record, LedgerValue = "record", Routable = false,
            NotRoutableReason = "a generic Dataverse record-write side-effect leg (distinct from the work_product host-record persistence) is not yet built — lands in a later wave",
        },
        new Entry
        {
            Disposition = BindingDisposition.Notification, LedgerValue = "notification", Routable = false,
            NotRoutableReason = "the notification-delivery side-effect leg is not yet built — lands in a later wave",
        },
    }.ToDictionary(e => e.Disposition);

    /// <summary>The registry row for a disposition. Throws on an unknown enum value (Binding/registry drift).</summary>
    public static Entry For(BindingDisposition disposition) =>
        Registry.TryGetValue(disposition, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(
                nameof(disposition), disposition,
                "Unknown BindingDisposition value — Binding contract / DispositionRoutability registry drift. " +
                "Register the disposition in DispositionRoutability (ADR-043 §3 — the ONE disposition source).");

    /// <summary>The ADR-040 ledger vocabulary member for a disposition (present for every disposition — store precedes routing).</summary>
    public static string ToLedgerValue(BindingDisposition disposition) => For(disposition).LedgerValue;

    /// <summary>Whether <see cref="OutputRouter"/> has a real routing leg for this disposition.</summary>
    public static bool IsRoutable(BindingDisposition disposition) => For(disposition).Routable;

    /// <summary>
    /// Whether the dispatch admit-gate admits this disposition. <b>ADR-043 §3</b>: admission IS
    /// routability — there is no separate admission list.
    /// </summary>
    public static bool IsAdmissible(BindingDisposition disposition) => For(disposition).Routable;

    /// <summary>The one-line rationale for a not-yet-routable disposition (null for routable ones).</summary>
    public static string? NotRoutableReason(BindingDisposition disposition) => For(disposition).NotRoutableReason;

    /// <summary>
    /// The loud post-store rejection message for a not-yet-routable disposition — single-sourced so the
    /// router's stub and any future caller emit the SAME message. The referenced entry WAS stored
    /// (ADR-040 storage-precedes-rendering); only the rendering leg is missing.
    /// </summary>
    public static string NotRoutableMessage(BindingDisposition disposition, string ledgerKey)
    {
        var entry = For(disposition);
        return $"OutputRouter: disposition '{entry.LedgerValue}' has no routing leg yet " +
               $"({entry.NotRoutableReason}). The output WAS stored to the session ledger as '{ledgerKey}' " +
               "(storage precedes rendering — ADR-040); only the rendering leg is missing. Do NOT work " +
               "around this by rendering inline — that would silently violate the disposition contract " +
               "(ADR-039 single-routing-surface / ADR-040 disposition-is-the-only-rendering-contract).";
    }

    /// <summary>Every registered disposition (for exhaustive seam/contract assertions).</summary>
    public static IReadOnlyCollection<BindingDisposition> All => (IReadOnlyCollection<BindingDisposition>)Registry.Keys;
}
