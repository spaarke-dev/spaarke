namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Abstraction for <see cref="Guid"/> generation, introduced as part of the Phase 4 Track C
/// determinism PoC (FR-13 / task 042).
/// </summary>
/// <remarks>
/// <para>
/// Pairs with .NET's BCL <see cref="System.TimeProvider"/> to provide two seams for deterministic
/// testing of <c>Services/Workspace/*</c>: a time seam (<see cref="System.TimeProvider"/>) and an
/// identity seam (<see cref="IGuidProvider"/>). Together they replace direct calls to
/// <c>DateTimeOffset.UtcNow</c>, <c>DateTime.UtcNow</c>, and <c>Guid.NewGuid()</c> in production
/// code paths whose tests need to assert against known values.
/// </para>
/// <para>
/// <b>ADR-010 (DI minimalism) compliance</b>: A bespoke interface is justified here because (a) it
/// has a genuine seam requirement — tests need a deterministic implementation distinct from the
/// production <see cref="DefaultGuidProvider"/>; (b) the BCL has no built-in <c>GuidProvider</c>
/// equivalent (unlike <see cref="System.TimeProvider"/>); (c) the production surface area is a
/// single method, keeping the interface minimal. This is the second of the "allowed seams"
/// listed in ADR-010 §Allowed Seams pattern (single-impl + test seam).
/// </para>
/// <para>
/// <b>Phase 4 PoC scope (D-04)</b>: this interface is greenfield as of Task 042. It is registered
/// in DI via <see cref="Infrastructure.DI.WorkspaceModule.AddWorkspaceServices"/>, but production
/// consumers are added incrementally — Phase 4 ships the abstraction + sibling
/// <see cref="System.TimeProvider"/> registration; production consumer migration (e.g.,
/// <c>MatterPreFillService.AnalyzeFilesAsync</c> requestId generation) is the r3 generalization
/// step per the testclock-pattern-draft findings doc.
/// </para>
/// </remarks>
public interface IGuidProvider
{
    /// <summary>
    /// Returns a new <see cref="Guid"/>. Production implementation delegates to
    /// <see cref="Guid.NewGuid"/>; test implementations return seeded values.
    /// </summary>
    Guid NewGuid();
}

/// <summary>
/// Production implementation of <see cref="IGuidProvider"/> that delegates to
/// <see cref="Guid.NewGuid"/>.
/// </summary>
/// <remarks>
/// Stateless and thread-safe — registered as a singleton. Replaces direct
/// <see cref="Guid.NewGuid"/> calls in production code so that tests can substitute a
/// deterministic implementation without touching the consuming class signatures.
/// </remarks>
public sealed class DefaultGuidProvider : IGuidProvider
{
    /// <inheritdoc />
    public Guid NewGuid() => Guid.NewGuid();
}
