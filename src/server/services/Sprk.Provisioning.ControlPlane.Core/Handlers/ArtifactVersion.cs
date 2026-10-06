// -----------------------------------------------------------------------------
// ArtifactVersion.cs
//
// Task 245b (G25 — run context, part 2). The ONE way a handler turns the bytes
// of an artifact it applies into the version component of its idempotency key:
// lowercase-hex SHA-256 of exactly those bytes.
//
//   H2a  infra-{customerId}-{v}      v = the ARM template JSON it deploys
//   H2b  aisearch-{customerId}-{v}   v = the index schema set it PUTs
//   H4   kv-{customerId}-{v}         v = the embedded secret-catalog manifest
//   H4b  appsettings-{env}-{v}       v = the same manifest (same bytes ⇒ same v)
//   H12a h12a-{customerId}-{v}       v = the seed manifest (FileSeedManifestReader)
//
// Before T245b the first four read their version from a run parameter nothing
// wrote (`bicepVer` / `indexVer` / `secretsVer`), so every real run stopped at
// H2a. A version computed from the artifact is also the only one that is
// correct: same artifact ⇒ same key (a resume is a no-op), changed artifact ⇒
// new key (the handler re-applies).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>Content-derived artifact versions for handler idempotency keys (task 245b).</summary>
public static class ArtifactVersion
{
    /// <summary>Lowercase-hex SHA-256 of <paramref name="bytes"/>.</summary>
    public static string Of(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Lowercase-hex SHA-256 of the UTF-8 encoding of <paramref name="text"/>, with line endings normalised to
    /// <c>\n</c> first — an embedded text artifact (manifest.yaml, index schemas) carries whatever line endings the
    /// build machine's checkout gave it, and the same content must give the same version on Windows and Linux
    /// builds. Binary artifacts (the downloaded ARM template, verified against CI's sha256) use the byte overload.
    /// </summary>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Of(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));
    }
}
