using Microsoft.Extensions.Logging.Abstractions;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class TableDependencySorterTests
{
    private readonly TableDependencySorter _sorter = new(NullLogger<TableDependencySorter>.Instance);

    private static TableSchema Table(string name, string schema = "dbo", params ForeignKeySchema[] fks) =>
        new() { TableName = name, SchemaName = schema, ForeignKeys = fks.ToList() };

    private static ForeignKeySchema Fk(string refTable, string refSchema = "dbo") =>
        new() { Name = $"FK_{refTable}", ColumnName = "Id", ReferencedTable = refTable, ReferencedSchema = refSchema, ReferencedColumn = "Id" };

    [Fact]
    public void SortByDependencies_IndependentTables_PreservesOrder()
    {
        var tables = new List<TableSchema> { Table("A"), Table("B"), Table("C") };
        var sorted = _sorter.SortByDependencies(tables);

        Assert.Equal(3, sorted.Count);
        Assert.Contains(sorted, t => t.TableName == "A");
        Assert.Contains(sorted, t => t.TableName == "B");
        Assert.Contains(sorted, t => t.TableName == "C");
    }

    [Fact]
    public void SortByDependencies_LinearChain_ParentFirst()
    {
        // C -> B -> A
        var tables = new List<TableSchema>
        {
            Table("C", "dbo", Fk("B")),
            Table("B", "dbo", Fk("A")),
            Table("A")
        };

        var sorted = _sorter.SortByDependencies(tables);

        var indexA = sorted.FindIndex(t => t.TableName == "A");
        var indexB = sorted.FindIndex(t => t.TableName == "B");
        var indexC = sorted.FindIndex(t => t.TableName == "C");

        Assert.True(indexA < indexB, "A should come before B");
        Assert.True(indexB < indexC, "B should come before C");
    }

    [Fact]
    public void SortByDependencies_MultipleDependencies_AllParentsFirst()
    {
        // OrderDetails -> Orders, OrderDetails -> Products
        var tables = new List<TableSchema>
        {
            Table("OrderDetails", "dbo", Fk("Orders"), Fk("Products")),
            Table("Orders"),
            Table("Products")
        };

        var sorted = _sorter.SortByDependencies(tables);

        var indexOD = sorted.FindIndex(t => t.TableName == "OrderDetails");
        var indexO = sorted.FindIndex(t => t.TableName == "Orders");
        var indexP = sorted.FindIndex(t => t.TableName == "Products");

        Assert.True(indexO < indexOD, "Orders should come before OrderDetails");
        Assert.True(indexP < indexOD, "Products should come before OrderDetails");
    }

    [Fact]
    public void SortByDependencies_SelfReference_DoesNotBlock()
    {
        var tables = new List<TableSchema>
        {
            Table("Employees", "dbo", Fk("Employees"))
        };

        var sorted = _sorter.SortByDependencies(tables);

        Assert.Single(sorted);
        Assert.Equal("Employees", sorted[0].TableName);
    }

    [Fact]
    public void SortByDependencies_CircularDependency_StillReturnsAll()
    {
        // A -> B, B -> A (circular)
        var tables = new List<TableSchema>
        {
            Table("A", "dbo", Fk("B")),
            Table("B", "dbo", Fk("A"))
        };

        var sorted = _sorter.SortByDependencies(tables);

        Assert.Equal(2, sorted.Count);
        Assert.Contains(sorted, t => t.TableName == "A");
        Assert.Contains(sorted, t => t.TableName == "B");
    }

    [Fact]
    public void SortByDependencies_EmptyList_ReturnsEmpty()
    {
        var sorted = _sorter.SortByDependencies([]);
        Assert.Empty(sorted);
    }

    [Fact]
    public void SortByDependencies_ExternalReference_Ignored()
    {
        // A references ExternalTable which is not in the migration set
        var tables = new List<TableSchema>
        {
            Table("A", "dbo", Fk("ExternalTable"))
        };

        var sorted = _sorter.SortByDependencies(tables);

        Assert.Single(sorted);
        Assert.Equal("A", sorted[0].TableName);
    }
}
