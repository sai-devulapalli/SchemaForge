using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;
using SchemaForge.Tests.Helpers;
using IDataReader = SchemaForge.Abstractions.Interfaces.IDataReader;

namespace SchemaForge.Tests;

public class BulkDataMigratorTests
{
    private readonly Mock<IDataReader> _mockDataReader = new();
    private readonly Mock<IDataWriter> _mockDataWriter = new();
    private readonly SnakeCaseConverter _namingConverter = TestServices.CreateSnakeCaseConverter();

    private BulkDataMigrator CreateMigrator(bool isDryRun = false)
    {
        var sqlCollector = new SqlCollector(isCollecting: isDryRun);

        var services = new ServiceCollection();
        services.AddKeyedTransient<IDataReader>("sqlserver", (_, _) => _mockDataReader.Object);
        services.AddKeyedTransient<IDataWriter>("postgres", (_, _) => _mockDataWriter.Object);
        var sp = services.BuildServiceProvider();

        return new BulkDataMigrator(
            Mock.Of<ILogger<BulkDataMigrator>>(),
            _namingConverter,
            sqlCollector,
            sp);
    }

    #region Argument Validation

    [Fact]
    public async Task MigrateDataAsync_NullSourceType_Throws()
    {
        var migrator = CreateMigrator();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            migrator.MigrateDataAsync(null!, "postgres", "src", "tgt", "public", [], 1000));
    }

    [Fact]
    public async Task MigrateDataAsync_EmptySourceType_Throws()
    {
        var migrator = CreateMigrator();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            migrator.MigrateDataAsync("", "postgres", "src", "tgt", "public", [], 1000));
    }

    [Fact]
    public async Task MigrateDataAsync_NullTargetType_Throws()
    {
        var migrator = CreateMigrator();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            migrator.MigrateDataAsync("sqlserver", null!, "src", "tgt", "public", [], 1000));
    }

    [Fact]
    public async Task MigrateDataAsync_ZeroBatchSize_Throws()
    {
        var migrator = CreateMigrator();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [], 0));
    }

    [Fact]
    public async Task MigrateDataAsync_NegativeBatchSize_Throws()
    {
        var migrator = CreateMigrator();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [], -1));
    }

    #endregion

    #region Normal Mode

    [Fact]
    public async Task MigrateDataAsync_DisablesAndReenablesConstraints()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int"), TestData.Column("Name", "varchar")],
                primaryKeys: ["Id"])
        };

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(0);

        var migrator = CreateMigrator();

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", tables, 1000);

        _mockDataWriter.Verify(w => w.DisableConstraintsAsync("tgt"), Times.Once);
        _mockDataWriter.Verify(w => w.EnableConstraintsAsync("tgt"), Times.Once);
    }

    [Fact]
    public async Task MigrateDataAsync_ReenablesConstraints_EvenOnFailure()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int")],
                primaryKeys: ["Id"])
        };

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ThrowsAsync(new Exception("Read failed"));

        var migrator = CreateMigrator();

        await Assert.ThrowsAsync<Exception>(() =>
            migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", tables, 1000));

        _mockDataWriter.Verify(w => w.EnableConstraintsAsync("tgt"), Times.Once);
    }

    [Fact]
    public async Task MigrateDataAsync_MigratesInBatches()
    {
        var table = TestData.Table("Users", "dbo",
            columns: [TestData.Column("Id", "int"), TestData.Column("Name", "varchar")],
            primaryKeys: ["Id"]);

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        dataTable.Columns.Add("Name", typeof(string));
        for (int i = 0; i < 100; i++)
            dataTable.Rows.Add(i, $"User{i}");

        var emptyTable = new DataTable();
        emptyTable.Columns.Add("Id", typeof(int));

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(250);

        _mockDataReader.SetupSequence(r => r.FetchBatchAsync(It.IsAny<string>(), It.IsAny<TableSchema>(), It.IsAny<int>(), 100))
            .ReturnsAsync(dataTable)
            .ReturnsAsync(dataTable)
            .ReturnsAsync(dataTable);

        var migrator = CreateMigrator();

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [table], 100);

        _mockDataWriter.Verify(w => w.BulkInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TableSchema>(), It.IsAny<DataTable>()), Times.Exactly(3));
        _mockDataWriter.Verify(w => w.ResetSequencesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TableSchema>()), Times.Once);
    }

    [Fact]
    public async Task MigrateDataAsync_SkipsEmptyTables()
    {
        var table = TestData.Table("Users", "dbo",
            columns: [TestData.Column("Id", "int")],
            primaryKeys: ["Id"]);

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(0);

        var migrator = CreateMigrator();

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [table], 1000);

        _mockDataReader.Verify(r => r.FetchBatchAsync(It.IsAny<string>(), It.IsAny<TableSchema>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region Dry Run Mode

    [Fact]
    public async Task MigrateDataAsync_DryRun_GeneratesSampleInserts()
    {
        var table = TestData.Table("Users", "dbo",
            columns: [TestData.Column("Id", "int"), TestData.Column("Name", "varchar")],
            primaryKeys: ["Id"]);

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        dataTable.Columns.Add("Name", typeof(string));
        dataTable.Rows.Add(1, "Alice");

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(10);
        _mockDataReader.Setup(r => r.FetchBatchAsync(It.IsAny<string>(), It.IsAny<TableSchema>(), 0, 1000))
            .ReturnsAsync(dataTable);

        var sqlCollector = new SqlCollector(isCollecting: true);
        var services = new ServiceCollection();
        services.AddKeyedTransient<IDataReader>("sqlserver", (_, _) => _mockDataReader.Object);
        var sp = services.BuildServiceProvider();

        var migrator = new BulkDataMigrator(
            Mock.Of<ILogger<BulkDataMigrator>>(),
            _namingConverter,
            sqlCollector,
            sp);

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [table], 1000);

        var statements = sqlCollector.GetStatements();
        var inserts = statements.Where(s => s.Category == "Data").ToList();
        Assert.NotEmpty(inserts);
        Assert.Contains("INSERT INTO", inserts[0].Sql);

        // Dry run should NOT call DataWriter
        _mockDataWriter.Verify(w => w.DisableConstraintsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MigrateDataAsync_DryRun_EmptyTable_AddsComment()
    {
        var table = TestData.Table("Users", "dbo",
            columns: [TestData.Column("Id", "int")],
            primaryKeys: ["Id"]);

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(0);

        var sqlCollector = new SqlCollector(isCollecting: true);
        var services = new ServiceCollection();
        services.AddKeyedTransient<IDataReader>("sqlserver", (_, _) => _mockDataReader.Object);
        var sp = services.BuildServiceProvider();

        var migrator = new BulkDataMigrator(
            Mock.Of<ILogger<BulkDataMigrator>>(),
            _namingConverter,
            sqlCollector,
            sp);

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [table], 1000);

        var comments = sqlCollector.GetStatements().Where(s => s.Category == "Comment").ToList();
        Assert.Contains(comments, c => c.Sql.Contains("No data"));
    }

    #endregion

    #region FormatValue (via dry run INSERT generation)

    [Fact]
    public async Task DryRun_FormatValue_NullBecomesNULL()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("Value", "varchar"),
            DBNull.Value);

        Assert.Contains("NULL", inserts);
    }

    [Fact]
    public async Task DryRun_FormatValue_IntegerNotQuoted()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("Id", "int"),
            42);

        Assert.Contains("42", inserts);
        Assert.DoesNotContain("'42'", inserts);
    }

    [Fact]
    public async Task DryRun_FormatValue_StringEscapesSingleQuotes()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("Name", "varchar"),
            "O'Brien");

        Assert.Contains("O''Brien", inserts);
    }

    [Fact]
    public async Task DryRun_FormatValue_BooleanFormattedCorrectly()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("IsActive", "bit"),
            true);

        Assert.Contains("TRUE", inserts);
    }

    [Fact]
    public async Task DryRun_FormatValue_DateFormattedCorrectly()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("CreatedAt", "datetime"),
            new DateTime(2024, 1, 15, 10, 30, 0));

        Assert.Contains("2024-01-15 10:30:00", inserts);
    }

    [Fact]
    public async Task DryRun_FormatValue_BinaryAsHex()
    {
        var inserts = await GenerateDryRunInsert(
            TestData.Column("Data", "binary"),
            new byte[] { 0xAB, 0xCD });

        Assert.Contains("0xABCD", inserts);
    }

    private async Task<string> GenerateDryRunInsert(ColumnSchema column, object value)
    {
        var table = TestData.Table("TestTable", "dbo",
            columns: [column],
            primaryKeys: []);

        var dataTable = new DataTable();
        dataTable.Columns.Add(column.ColumnName, value is DBNull ? typeof(object) : value.GetType());
        dataTable.Rows.Add(value);

        _mockDataReader.Setup(r => r.GetRowCountAsync(It.IsAny<string>(), It.IsAny<TableSchema>()))
            .ReturnsAsync(1);
        _mockDataReader.Setup(r => r.FetchBatchAsync(It.IsAny<string>(), It.IsAny<TableSchema>(), 0, It.IsAny<int>()))
            .ReturnsAsync(dataTable);

        var sqlCollector = new SqlCollector(isCollecting: true);
        var services = new ServiceCollection();
        services.AddKeyedTransient<IDataReader>("sqlserver", (_, _) => _mockDataReader.Object);
        var sp = services.BuildServiceProvider();

        var migrator = new BulkDataMigrator(
            Mock.Of<ILogger<BulkDataMigrator>>(),
            _namingConverter,
            sqlCollector,
            sp);

        await migrator.MigrateDataAsync("sqlserver", "postgres", "src", "tgt", "public", [table], 1000);

        return string.Join("\n", sqlCollector.GetStatements().Where(s => s.Category == "Data").Select(s => s.Sql));
    }

    #endregion
}
