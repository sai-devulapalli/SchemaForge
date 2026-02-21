namespace SchemaForge.PackageTests;

/// <summary>
/// Centralised connection strings matching docker-compose.test.yml
/// </summary>
internal static class ConnectionStrings
{
    public const string SqlServer =
        "Server=localhost,1434;Database=schemaforge_test;User Id=sa;Password=SchemaForge@Test1;TrustServerCertificate=True;";

    public const string Postgres =
        "Host=localhost;Port=5434;Database=schemaforge_test;Username=postgres;Password=SchemaForgeTest1;";

    public const string MySql =
        "Server=localhost;Port=3307;Database=schemaforge_test;User Id=root;Password=SchemaForgeTest1;";

    public const string Oracle =
        "User Id=testuser;Password=SchemaForgeTest1;Data Source=localhost:1522/FREEPDB1;";
}
