using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Builder;

namespace SchemaForge.Tests;

public class MigrationBuilderTests
{
    #region Source Configuration

    [Fact]
    public void FromSqlServer_SetsSourceType()
    {
        var builder = new MigrationBuilder().FromSqlServer("Server=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.SqlServer, settings.SourceDatabaseType);
        Assert.Equal("Server=localhost", settings.SourceConnectionString);
    }

    [Fact]
    public void FromPostgres_SetsSourceType()
    {
        var builder = new MigrationBuilder().FromPostgres("Host=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.PostgreSql, settings.SourceDatabaseType);
    }

    [Fact]
    public void FromMySql_SetsSourceType()
    {
        var builder = new MigrationBuilder().FromMySql("Server=localhost");
        Assert.Equal(DatabaseTypes.MySql, builder.GetSettings().SourceDatabaseType);
    }

    [Fact]
    public void FromOracle_SetsSourceType()
    {
        var builder = new MigrationBuilder().FromOracle("Data Source=localhost");
        Assert.Equal(DatabaseTypes.Oracle, builder.GetSettings().SourceDatabaseType);
    }

    [Fact]
    public void From_SetsTypeAndConnectionString()
    {
        var builder = new MigrationBuilder().From("POSTGRES", "Host=localhost");
        var settings = builder.GetSettings();

        Assert.Equal("postgres", settings.SourceDatabaseType);
        Assert.Equal("Host=localhost", settings.SourceConnectionString);
    }

    #endregion

    #region Target Configuration

    [Fact]
    public void ToSqlServer_SetsTargetWithDefaultSchema()
    {
        var builder = new MigrationBuilder().ToSqlServer("Server=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.SqlServer, settings.TargetDatabaseType);
        Assert.Equal("dbo", settings.TargetSchemaName);
    }

    [Fact]
    public void ToPostgres_SetsTargetWithDefaultSchema()
    {
        var builder = new MigrationBuilder().ToPostgres("Host=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.PostgreSql, settings.TargetDatabaseType);
        Assert.Equal("public", settings.TargetSchemaName);
    }

    [Fact]
    public void ToMySql_SetsTarget()
    {
        var builder = new MigrationBuilder().ToMySql("Server=localhost", "mydb");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.MySql, settings.TargetDatabaseType);
        Assert.Equal("mydb", settings.TargetSchemaName);
    }

    [Fact]
    public void ToOracle_SetsTarget()
    {
        var builder = new MigrationBuilder().ToOracle("Data Source=localhost", "MYSCHEMA");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.Oracle, settings.TargetDatabaseType);
        Assert.Equal("MYSCHEMA", settings.TargetSchemaName);
    }

    [Fact]
    public void To_SetsTargetExplicitly()
    {
        var builder = new MigrationBuilder().To("POSTGRES", "Host=localhost", "custom_schema");
        var settings = builder.GetSettings();

        Assert.Equal("postgres", settings.TargetDatabaseType);
        Assert.Equal("custom_schema", settings.TargetSchemaName);
    }

    #endregion

    #region Migration Options

    [Fact]
    public void MigrateAll_EnablesAllOptions()
    {
        var builder = new MigrationBuilder().MigrateAll();
        var options = builder.GetOptions();

        Assert.True(options.MigrateSchema);
        Assert.True(options.MigrateData);
        Assert.True(options.MigrateViews);
        Assert.True(options.MigrateIndexes);
        Assert.True(options.MigrateConstraints);
        Assert.True(options.MigrateForeignKeys);
    }

    [Fact]
    public void MigrateSchemaOnly_EnablesSchemaDisablesData()
    {
        var builder = new MigrationBuilder().MigrateSchemaOnly();
        var options = builder.GetOptions();

        Assert.True(options.MigrateSchema);
        Assert.False(options.MigrateData);
        Assert.True(options.MigrateViews);
        Assert.True(options.MigrateIndexes);
    }

    [Fact]
    public void MigrateDataOnly_DisablesSchemaEnablesData()
    {
        var builder = new MigrationBuilder().MigrateDataOnly();
        var options = builder.GetOptions();

        Assert.False(options.MigrateSchema);
        Assert.True(options.MigrateData);
        Assert.False(options.MigrateViews);
        Assert.False(options.MigrateIndexes);
        Assert.False(options.MigrateConstraints);
        Assert.False(options.MigrateForeignKeys);
    }

    [Fact]
    public void WithSchema_EnablesSchemaOption()
    {
        var builder = new MigrationBuilder().MigrateDataOnly().WithSchema();
        Assert.True(builder.GetOptions().MigrateSchema);
    }

    [Fact]
    public void WithoutSchema_DisablesSchemaOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutSchema();
        Assert.False(builder.GetOptions().MigrateSchema);
    }

    [Fact]
    public void WithData_EnablesDataOption()
    {
        var builder = new MigrationBuilder().MigrateSchemaOnly().WithData();
        Assert.True(builder.GetOptions().MigrateData);
    }

    [Fact]
    public void WithoutData_DisablesDataOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutData();
        Assert.False(builder.GetOptions().MigrateData);
    }

    [Fact]
    public void WithViews_EnablesViewsOption()
    {
        var builder = new MigrationBuilder().MigrateDataOnly().WithViews();
        Assert.True(builder.GetOptions().MigrateViews);
    }

    [Fact]
    public void WithoutViews_DisablesViewsOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutViews();
        Assert.False(builder.GetOptions().MigrateViews);
    }

    [Fact]
    public void WithIndexes_EnablesIndexesOption()
    {
        var builder = new MigrationBuilder().MigrateDataOnly().WithIndexes();
        Assert.True(builder.GetOptions().MigrateIndexes);
    }

    [Fact]
    public void WithoutIndexes_DisablesIndexesOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutIndexes();
        Assert.False(builder.GetOptions().MigrateIndexes);
    }

    [Fact]
    public void WithConstraints_EnablesConstraintsOption()
    {
        var builder = new MigrationBuilder().MigrateDataOnly().WithConstraints();
        Assert.True(builder.GetOptions().MigrateConstraints);
    }

    [Fact]
    public void WithoutConstraints_DisablesConstraintsOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutConstraints();
        Assert.False(builder.GetOptions().MigrateConstraints);
    }

    [Fact]
    public void WithForeignKeys_EnablesForeignKeysOption()
    {
        var builder = new MigrationBuilder().MigrateDataOnly().WithForeignKeys();
        Assert.True(builder.GetOptions().MigrateForeignKeys);
    }

    [Fact]
    public void WithoutForeignKeys_DisablesForeignKeysOption()
    {
        var builder = new MigrationBuilder().MigrateAll().WithoutForeignKeys();
        Assert.False(builder.GetOptions().MigrateForeignKeys);
    }

    #endregion

    #region Table Filtering

    [Fact]
    public void IncludeTables_SetsIncludeList()
    {
        var builder = new MigrationBuilder().IncludeTables("Users", "Orders");
        var options = builder.GetOptions();

        Assert.Equal(2, options.IncludeTables.Count);
        Assert.Contains("Users", options.IncludeTables);
        Assert.Contains("Orders", options.IncludeTables);
    }

    [Fact]
    public void IncludeTables_FromEnumerable_SetsIncludeList()
    {
        var tables = new List<string> { "Users", "Orders" };
        var builder = new MigrationBuilder().IncludeTables(tables);

        Assert.Equal(2, builder.GetOptions().IncludeTables.Count);
    }

    [Fact]
    public void ExcludeTables_SetsExcludeList()
    {
        var builder = new MigrationBuilder().ExcludeTables("AuditLog", "TempData");
        var options = builder.GetOptions();

        Assert.Equal(2, options.ExcludeTables.Count);
        Assert.Contains("AuditLog", options.ExcludeTables);
    }

    [Fact]
    public void ExcludeTables_FromEnumerable_SetsExcludeList()
    {
        var tables = new List<string> { "AuditLog" };
        var builder = new MigrationBuilder().ExcludeTables(tables);

        Assert.Single(builder.GetOptions().ExcludeTables);
    }

    #endregion

    #region Performance & Behavior

    [Fact]
    public void WithBatchSize_SetsValidBatchSize()
    {
        var builder = new MigrationBuilder().WithBatchSize(5000);
        Assert.Equal(5000, builder.GetOptions().DataBatchSize);
        Assert.Equal(5000, builder.GetSettings().BatchSize);
    }

    [Fact]
    public void WithBatchSize_Zero_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MigrationBuilder().WithBatchSize(0));
    }

    [Fact]
    public void WithBatchSize_Negative_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MigrationBuilder().WithBatchSize(-1));
    }

    [Fact]
    public void ContinueOnError_SetsContinueOnErrorTrue()
    {
        var builder = new MigrationBuilder().ContinueOnError();
        Assert.True(builder.GetOptions().ContinueOnError);
    }

    [Fact]
    public void StopOnError_SetsContinueOnErrorFalse()
    {
        var builder = new MigrationBuilder().StopOnError();
        Assert.False(builder.GetOptions().ContinueOnError);
    }

    #endregion

    #region Dry Run

    [Fact]
    public void DryRun_EnablesDryRunMode()
    {
        var builder = new MigrationBuilder().DryRun();
        Assert.True(builder.GetOptions().DryRun.Enabled);
    }

    [Fact]
    public void DryRun_WithOutputPath_SetsPath()
    {
        var builder = new MigrationBuilder().DryRun("/tmp/output.sql");
        var options = builder.GetOptions();

        Assert.True(options.DryRun.Enabled);
        Assert.Equal("/tmp/output.sql", options.DryRun.OutputFilePath);
    }

    [Fact]
    public void WithDataSamples_EnablesSamplesWithCount()
    {
        var builder = new MigrationBuilder().WithDataSamples(10);
        var options = builder.GetOptions();

        Assert.True(options.DryRun.IncludeDataSamples);
        Assert.Equal(10, options.DryRun.SampleRowCount);
    }

    [Fact]
    public void WithoutDataSamples_DisablesSamples()
    {
        var builder = new MigrationBuilder().WithoutDataSamples();
        Assert.False(builder.GetOptions().DryRun.IncludeDataSamples);
    }

    #endregion

    #region Naming Convention

    [Fact]
    public void WithNamingConvention_SnakeCase_SetsCorrectValue()
    {
        var builder = new MigrationBuilder().WithNamingConvention(NamingConvention.SnakeCase);
        Assert.Equal("snake_case", builder.GetSettings().NamingConvention);
    }

    [Fact]
    public void WithNamingConvention_PascalCase_SetsCorrectValue()
    {
        var builder = new MigrationBuilder().WithNamingConvention(NamingConvention.PascalCase);
        Assert.Equal("PascalCase", builder.GetSettings().NamingConvention);
    }

    [Fact]
    public void WithAutoNaming_SetsAutoAndStandards()
    {
        var builder = new MigrationBuilder().WithAutoNaming();
        var settings = builder.GetSettings();

        Assert.Equal("auto", settings.NamingConvention);
        Assert.True(settings.UseTargetDatabaseStandards);
    }

    [Fact]
    public void PreserveNames_SetsPreserveAndFlag()
    {
        var builder = new MigrationBuilder().PreserveNames();
        var settings = builder.GetSettings();

        Assert.Equal("preserve", settings.NamingConvention);
        Assert.True(settings.PreserveSourceCase);
    }

    [Fact]
    public void WithMaxIdentifierLength_SetsLength()
    {
        var builder = new MigrationBuilder().WithMaxIdentifierLength(63);
        Assert.Equal(63, builder.GetSettings().MaxIdentifierLength);
    }

    #endregion

    #region Validation

    [Fact]
    public void Build_MissingSource_ThrowsInvalidOperation()
    {
        var builder = new MigrationBuilder()
            .ToPostgres("Host=localhost");

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Source connection string is required", ex.Message);
    }

    [Fact]
    public void Build_MissingTarget_ThrowsInvalidOperation()
    {
        var builder = new MigrationBuilder()
            .FromSqlServer("Server=localhost");

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Target connection string is required", ex.Message);
    }

    [Fact]
    public void Build_MissingBoth_AccumulatesAllErrors()
    {
        var builder = new MigrationBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Source connection string", ex.Message);
        Assert.Contains("Target connection string", ex.Message);
    }

    #endregion

    #region Fluent Chaining

    [Fact]
    public void FluentChaining_ReturnsBuilder()
    {
        var builder = new MigrationBuilder()
            .FromSqlServer("Server=localhost")
            .ToPostgres("Host=localhost")
            .MigrateAll()
            .WithBatchSize(500)
            .IncludeTables("Users")
            .ContinueOnError()
            .DryRun();

        var settings = builder.GetSettings();
        var options = builder.GetOptions();

        Assert.Equal(DatabaseTypes.SqlServer, settings.SourceDatabaseType);
        Assert.Equal(DatabaseTypes.PostgreSql, settings.TargetDatabaseType);
        Assert.True(options.MigrateSchema);
        Assert.Equal(500, options.DataBatchSize);
        Assert.Single(options.IncludeTables);
        Assert.True(options.ContinueOnError);
        Assert.True(options.DryRun.Enabled);
    }

    #endregion
}
