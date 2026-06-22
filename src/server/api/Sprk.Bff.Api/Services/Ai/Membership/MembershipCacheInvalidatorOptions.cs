// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Configuration for the membership cache invalidator (FR-2P2.8 + AC-1P2.7).
//
// Operators flip Enabled=true when Redis is configured in the environment
// (the Redis ConnectionString lives in the existing Redis:* section read
// by CacheModule.cs). Default Enabled=false registers the Null peer per
// ADR-032 — safe for local dev (no Redis) and CI without conditional
// branches at the consumer site.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; .claude/adr/ADR-032-bff-nullobject-kill-switch.md.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Configuration for <see cref="MembershipCacheInvalidator"/>. Binds from
/// the <c>"Membership:CacheInvalidator"</c> appsettings section.
/// </summary>
public sealed class MembershipCacheInvalidatorOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Membership:CacheInvalidator";

    /// <summary>
    /// Default Redis channel name for membership cache invalidations
    /// (spec FR-2P2.8). Kept as a const so subscribers + publishers stay
    /// in sync even when an operator never overrides
    /// <see cref="Channel"/>.
    /// </summary>
    public const string DefaultChannel = "membership-cache-invalidate";

    /// <summary>
    /// Master kill switch. <c>false</c> by default — registers
    /// <see cref="NullMembershipCacheInvalidator"/> (logs +
    /// returns; no Redis interaction) and skips subscriber wiring.
    /// Operators flip to <c>true</c> in environments where
    /// <c>Redis:Enabled=true</c> AND the connection string is configured.
    /// </summary>
    /// <remarks>
    /// The DI module also guards on <c>IConnectionMultiplexer</c> being
    /// resolvable from the container (CacheModule only registers it when
    /// Redis is enabled). Either gate failing → Null peer wins; safe for
    /// local dev + CI without conditional branches at the consumer site.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Redis channel name used for both publish + subscribe. Defaults to
    /// <see cref="DefaultChannel"/> (<c>membership-cache-invalidate</c>)
    /// per spec FR-2P2.8.
    /// </summary>
    public string Channel { get; set; } = DefaultChannel;
}
