using Xunit;

namespace SchemaForge.PackageTests;

/// <summary>
/// Disables parallel execution for all PackageTests-collection tests.
/// All tests share the same Docker database containers, so concurrent writes
/// would corrupt shared state.
/// </summary>
[CollectionDefinition("PackageTests", DisableParallelization = true)]
public class PackageTestsCollection { }
