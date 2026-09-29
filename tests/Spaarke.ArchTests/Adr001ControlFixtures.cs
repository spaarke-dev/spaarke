// Control fixtures for ADR001_MinimalApiTests' package rule: a type in a Functions-shaped namespace, a type that
// depends on it (the negative control), and a type that does not (the positive control). Declared here, in the
// test assembly, so the controls need no Functions package — NetArchTest matches the dependency by namespace.

namespace Microsoft.Azure.Functions.Adr001Seeded
{
    internal sealed class SeededFunctionsType
    {
        public int Value { get; set; }
    }
}

namespace Spaarke.ArchTests
{
    internal sealed class Adr001SeededFunctionsDependent
    {
        private readonly Microsoft.Azure.Functions.Adr001Seeded.SeededFunctionsType _seeded = new();

        public int Read() => _seeded.Value;
    }

    internal sealed class Adr001SanctionedType
    {
        private readonly int _value = 42;

        public int Read() => _value;
    }
}
