// -----------------------------------------------------------------------------
// IAiContentSeeder.cs
//
// H12a seam for the AI content writer (L56 / W2). Two implementations, parity
// with ISeedManifestRunner: DataverseWebApiAiContentSeeder (production) and the
// H12a unit tests' fake.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>Writes the embedded AI content set into a customer environment, create-if-missing, with fixed primary keys.</summary>
public interface IAiContentSeeder
{
    /// <summary>SHA-256 of the embedded content files (lower hex); H12a folds it into its idempotency key.</summary>
    string ContentHash { get; }

    /// <summary>Seeds the content. Never updates or deletes an existing row.</summary>
    Task<AiContentSeedOutcome> SeedAsync(SeedManifestInvocationRequest request, CancellationToken cancellationToken);
}

/// <summary>What the AI content seed did.</summary>
public abstract record AiContentSeedOutcome
{
    private AiContentSeedOutcome()
    {
    }

    /// <summary>Every row, link and canvas is present (created now or already there). <paramref name="Summary"/> is for runNotes.</summary>
    public sealed record Success(string Summary) : AiContentSeedOutcome;

    /// <summary>The set has a missing dependency (or is malformed); nothing was written.</summary>
    public sealed record Refused(string Diagnostic) : AiContentSeedOutcome;

    /// <summary>A Dataverse call failed part-way. Rows already created stay; a re-run finds them and continues.</summary>
    public sealed record Failed(string Diagnostic) : AiContentSeedOutcome;
}
