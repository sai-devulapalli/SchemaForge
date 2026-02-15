using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;
using SchemaForge.Tests.Helpers;

namespace SchemaForge.Tests;

public class MigrationOrchestratorTests
{
    private readonly Mock<ISchemaReader> _mockSchemaReader = new();
    private readonly Mock<ISchemaWriter> _mockSchemaWriter = new();
    private readonly Mock<IDataMigrator> _mockDataMigrator = new();
    private readonly SqlCollector _sqlCollector = new(isCollecting: false);

    private MigrationOrchestrator CreateOrchestrator(
        MigrationSettings? settings = null,
        MigrationOptions? options = null)
    {
        settings ??= new MigrationSettings
        {
            SourceDatabaseType = "sqlserver",
            TargetDatabaseType = "postgres",
            SourceConnectionString = "Server=source",
            TargetConnectionString = "Host=target",
            TargetSchemaName = "public"
        };

        options ??= new MigrationOptions();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedTransient<ISchemaReader>("sqlserver", (_, _) => _mockSchemaReader.Object);
        services.AddKeyedTransient<ISchemaWriter>("postgres", (_, _) => _mockSchemaWriter.Object);
        var sp = services.BuildServiceProvider();

        return new MigrationOrchestrator(
            Mock.Of<ILogger<MigrationOrchestrator>>(),
            Options.Create(settings),
            Options.Create(options),
            sp,
            _mockDataMigrator.Object,
            TestServices.CreateDependencySorter(),
            _sqlCollector);
    }

    private void SetupSchemaReader(List<TableSchema>? tables = null, List<ViewSchema>? views = null)
    {
        _mockSchemaReader.Setup(r => r.ReadSchemaAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<string>?>()))
            .ReturnsAsync(tables ?? []);
        _mockSchemaReader.Setup(r => r.ReadViewsAsync(It.IsAny<string>()))
            .ReturnsAsync(views ?? []);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_CallsAllSteps_WhenAllEnabled()
    {
        SetupSchemaReader(
            [new TableSchema { TableName = "Users", SchemaName = "dbo" }],
            [new ViewSchema { ViewName = "V1", SchemaName = "dbo", Definition = "SELECT 1" }]);

        var orchestrator = CreateOrchestrator();

        await orchestrator.ExecuteMigrationAsync();

        _mockSchemaReader.Verify(r => r.ReadSchemaAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<string>?>()), Times.Once);
        _mockSchemaWriter.Verify(w => w.CreateSchemaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<TableSchema>>()), Times.Once);
        _mockDataMigrator.Verify(m => m.MigrateDataAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<List<TableSchema>>(), It.IsAny<int>()), Times.Once);
        _mockSchemaWriter.Verify(w => w.CreateIndexesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<IndexSchema>>()), Times.Once);
        _mockSchemaWriter.Verify(w => w.CreateConstraintsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<ConstraintSchema>>()), Times.Once);
        _mockSchemaWriter.Verify(w => w.CreateViewsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<ViewSchema>>(), It.IsAny<List<TableSchema>?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_SkipsSchema_WhenDisabled()
    {
        SetupSchemaReader();
        var options = new MigrationOptions { MigrateSchema = false };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaWriter.Verify(w => w.CreateSchemaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<TableSchema>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_SkipsData_WhenDisabled()
    {
        SetupSchemaReader();
        var options = new MigrationOptions { MigrateData = false };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockDataMigrator.Verify(m => m.MigrateDataAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<List<TableSchema>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_SkipsIndexes_WhenDisabled()
    {
        SetupSchemaReader();
        var options = new MigrationOptions { MigrateIndexes = false };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaWriter.Verify(w => w.CreateIndexesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<IndexSchema>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_SkipsConstraints_WhenDisabled()
    {
        SetupSchemaReader();
        var options = new MigrationOptions { MigrateConstraints = false };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaWriter.Verify(w => w.CreateConstraintsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<ConstraintSchema>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_SkipsViews_WhenDisabled()
    {
        SetupSchemaReader();
        var options = new MigrationOptions { MigrateViews = false };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaWriter.Verify(w => w.CreateViewsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<ViewSchema>>(), It.IsAny<List<TableSchema>?>()), Times.Never);
        _mockSchemaReader.Verify(r => r.ReadViewsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_CancellationToken_ThrowsOperationCanceled()
    {
        SetupSchemaReader([new TableSchema { TableName = "Users", SchemaName = "dbo" }]);

        var orchestrator = CreateOrchestrator();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            orchestrator.ExecuteMigrationAsync(new MigrationOptions(), cts.Token));
    }

    [Fact]
    public async Task ExecuteMigrationAsync_ContinueOnError_DoesNotThrow()
    {
        _mockSchemaReader.Setup(r => r.ReadSchemaAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<string>?>()))
            .ThrowsAsync(new Exception("Read failed"));

        var options = new MigrationOptions { ContinueOnError = true };
        var orchestrator = CreateOrchestrator(options: options);

        // Should not throw when ContinueOnError is true
        await orchestrator.ExecuteMigrationAsync(options);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_StopOnError_Throws()
    {
        _mockSchemaReader.Setup(r => r.ReadSchemaAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<string>?>()))
            .ThrowsAsync(new Exception("Read failed"));

        var options = new MigrationOptions { ContinueOnError = false };
        var orchestrator = CreateOrchestrator(options: options);

        await Assert.ThrowsAsync<Exception>(() =>
            orchestrator.ExecuteMigrationAsync(options));
    }

    [Fact]
    public async Task ExecuteDryRunAsync_ReturnsDryRunResult()
    {
        SetupSchemaReader([new TableSchema { TableName = "Users", SchemaName = "dbo" }]);

        var dryRunCollector = new SqlCollector(isCollecting: true);
        dryRunCollector.AddSql("CREATE TABLE users", "Tables", "users");
        dryRunCollector.AddSql("CREATE INDEX idx", "Indexes", "idx");

        var settings = new MigrationSettings
        {
            SourceDatabaseType = "sqlserver",
            TargetDatabaseType = "postgres",
            SourceConnectionString = "Server=source",
            TargetConnectionString = "Host=target",
            TargetSchemaName = "public"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedTransient<ISchemaReader>("sqlserver", (_, _) => _mockSchemaReader.Object);
        services.AddKeyedTransient<ISchemaWriter>("postgres", (_, _) => _mockSchemaWriter.Object);
        var sp = services.BuildServiceProvider();

        var orchestrator = new MigrationOrchestrator(
            Mock.Of<ILogger<MigrationOrchestrator>>(),
            Options.Create(settings),
            Options.Create(new MigrationOptions()),
            sp,
            _mockDataMigrator.Object,
            TestServices.CreateDependencySorter(),
            dryRunCollector);

        var options = new MigrationOptions();
        var result = await orchestrator.ExecuteDryRunAsync(options);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Script);
        Assert.Equal(1, result.Summary.TableCount);
        Assert.Equal(1, result.Summary.IndexCount);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_ReadsViewsOnlyWhenEnabled()
    {
        SetupSchemaReader([], [new ViewSchema { ViewName = "V1", SchemaName = "dbo", Definition = "SELECT 1" }]);
        var options = new MigrationOptions { MigrateViews = true };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaReader.Verify(r => r.ReadViewsAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_PassesTableFilters_ToSchemaReader()
    {
        SetupSchemaReader();
        var options = new MigrationOptions
        {
            IncludeTables = ["Users", "Orders"]
        };
        var orchestrator = CreateOrchestrator(options: options);

        await orchestrator.ExecuteMigrationAsync(options);

        _mockSchemaReader.Verify(r => r.ReadSchemaAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyList<string>?>(l => l != null && l.Count == 2),
            It.IsAny<IReadOnlyList<string>?>()), Times.Once);
    }
}
