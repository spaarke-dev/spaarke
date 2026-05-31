// SAFE-TO-REMOVE 2026-05-31
// Ephemeral verification test authored by task 023 (P1.D4 — CI gate negative-path verification, FR-12).
// This file is created on branch `test/ci-gate-negative-path-verification`, used to confirm the
// restored CI gate blocks merging when `Build & Test (Release)` fails, and is then DELETED
// (branch closed without merging). It must NEVER appear on `work/sdap-bff.api-test-suite-repair`
// or `master`.
using Xunit;

namespace Sprk.Bff.Api.Tests;

public class _CiGateVerificationTests
{
    [Fact]
    [Trait("status", "ci-gate-verification-only-delete")]
    public void CiGate_NegativePath_VerificationOnly_DELETE_ME()
    {
        Assert.True(false, "intentional failure to verify CI gate — task 023; safe to delete");
    }
}
