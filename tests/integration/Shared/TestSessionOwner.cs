/// <summary>
/// The well-known Entra <c>oid</c> that test fixtures authenticate as, and that sessions minted in
/// test bodies are owned by (issue #863).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately in the GLOBAL namespace: it is referenced from ~20 suites across
/// <c>integration/seam</c>, <c>integration/contract</c> and <c>integration/regression</c>, and a
/// constant whose only job is to be the same value everywhere should not need a using directive to
/// stay the same value everywhere.
/// </para>
/// <para>
/// <b>Why a constant and not <c>Guid.NewGuid()</c>.</b> Several fake auth handlers minted a fresh
/// oid per REQUEST. Entra never does that — an <c>oid</c> is stable per user per tenant, which is
/// the entire property that makes it an ownership key. A per-request identity meant every suite
/// exercised "session created by one user, read by another" on every single call, without anyone
/// noticing, because nothing checked ownership. The moment something did, those suites would fail
/// and the fixture — not the guard — would be the reason. Per
/// <c>.claude/constraints/bff-extensions.md</c> §F.2 (Fixture-Config-FIRST), a fixture emitting a
/// non-contract value is the defect: repair the fixture, never compensate in the assertions.
/// </para>
/// </remarks>
internal static class TestSessionOwner
{
    /// <summary>
    /// The owner every fixture authenticates as. Stable across requests, by contract.
    /// </summary>
    /// <remarks>
    /// Deliberately the value three existing fixtures already hardcoded
    /// (<c>ChatAckEndpointsContractTests</c>, <c>ChatDocumentEndpointsContractTests</c>,
    /// <c>SummarizeSessionEndpointContractTests</c>) rather than a fresh one. Picking a new value
    /// would have meant editing those three to agree with a constant, when the constant can simply
    /// agree with them — fewer files touched, and the suites that were already consistent stay
    /// untouched.
    /// </remarks>
    public const string Oid = "00000000-0000-0000-0000-000000000aaa";

    /// <summary>
    /// A DIFFERENT user in the same tenant — for asserting that ownership actually denies. A test
    /// that only ever uses <see cref="Oid"/> proves the owner can get in, never that anyone else
    /// cannot, and that second half is the one #863 was missing.
    /// </summary>
    public const string OtherOid = "00000000-0000-0000-0000-0000000000ff";
}
