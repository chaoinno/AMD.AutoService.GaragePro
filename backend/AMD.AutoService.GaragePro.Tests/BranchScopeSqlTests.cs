using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class BranchScopeSqlFactAttribute : FactAttribute
{
    public BranchScopeSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GARAGEPRO_BRANCH_SQL_CONNECTION")))
            Skip = "Set GARAGEPRO_BRANCH_SQL_CONNECTION to run SQL scope tests using connection-local temporary tables only.";
    }
}

public sealed class BranchScopeSqlTests
{
    [BranchScopeSqlFact]
    public async Task Scope_is_branch_local_and_shared_customers_do_not_expose_foreign_cars()
    {
        await using var db = new SqlConnection(Environment.GetEnvironmentVariable("GARAGEPRO_BRANCH_SQL_CONNECTION"));
        await db.OpenAsync();
        // No persistent tables or real customer records are touched.
        await db.ExecuteAsync("""
            CREATE TABLE #Staff (Id bigint PRIMARY KEY, BranchId int);
            CREATE TABLE #User (Id bigint PRIMARY KEY, StaffId bigint);
            CREATE TABLE #Customer (Id bigint PRIMARY KEY, LastUserId bigint);
            CREATE TABLE #Car (Id bigint PRIMARY KEY, UpdatedBy bigint);
            CREATE TABLE #CarCustomer (CustomerId bigint, CarId bigint, UpdatedBy bigint, Status int);
            CREATE TABLE #PJCarPickUp (CustomerId bigint, CarId bigint, BranchId int);
            INSERT #Staff VALUES (1,10),(2,20);
            INSERT #User VALUES (1,1),(2,2);
            INSERT #Customer VALUES (100,1),(200,2),(300,2),(400,2),(500,2);
            INSERT #Car VALUES (1000,1),(2000,2),(3000,2),(4000,2),(5000,2);
            INSERT #CarCustomer VALUES (100,1000,1,1),(200,2000,2,1),(300,3000,2,1),
                (100,4000,2,1),(500,5000,1,0);
            INSERT #PJCarPickUp VALUES (300,3000,10);
            """);

        async Task<long[]> Ids(string sql, int branch) =>
            (await db.QueryAsync<long>($"SELECT DISTINCT Id FROM ({TemporaryTables(sql)}) scoped ORDER BY Id",
                new { BranchId = branch })).ToArray();

        Assert.Equal(new long[] { 100, 300 }, await Ids(BranchDataScope.CustomerIds, 10));
        Assert.Equal(new long[] { 1000, 3000 }, await Ids(BranchDataScope.VehicleIds, 10));
        Assert.Equal(new long[] { 100, 200, 300, 400, 500 }, await Ids(BranchDataScope.CustomerIds, 20));
        Assert.Equal(new long[] { 2000, 3000, 4000, 5000 }, await Ids(BranchDataScope.VehicleIds, 20));
        Assert.Empty(await Ids(BranchDataScope.CustomerIds, 30));
        Assert.Empty(await Ids(BranchDataScope.VehicleIds, 30));
        Assert.Empty(await Ids(BranchDataScope.CustomerIds, 0));

        // The same predicates protect direct-ID writes, not only list queries.
        var customerScope = TemporaryTables(BranchDataScope.CustomerIds);
        var vehicleScope = TemporaryTables(BranchDataScope.VehicleIds);
        Assert.Equal(0, await db.ExecuteAsync(
            $"UPDATE #Customer SET LastUserId=1 WHERE Id=200 AND Id IN ({customerScope})",
            new { BranchId = 10 }));
        Assert.Equal(0, await db.ExecuteAsync(
            $"UPDATE #Car SET UpdatedBy=1 WHERE Id=4000 AND Id IN ({vehicleScope})",
            new { BranchId = 10 }));
        Assert.Equal(1, await db.ExecuteAsync(
            $"UPDATE #Car SET UpdatedBy=1 WHERE Id=1000 AND Id IN ({vehicleScope})",
            new { BranchId = 10 }));
    }

    private static string TemporaryTables(string sql)
    {
        var result = sql.Replace("[User]", "[#User]", StringComparison.Ordinal);
        foreach (var name in new[] { "Customer", "Staff", "CarCustomer", "PJCarPickUp", "Car" })
            result = Regex.Replace(result, $@"\b{name}\b", $"#{name}");
        return result;
    }
}
