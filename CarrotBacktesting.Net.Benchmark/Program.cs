// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using System.Data;

Console.WriteLine("Hello, World!");

BenchmarkRunner.Run<BackTestingEngineBenchmark>();

[MemoryDiagnoser]
public class BackTestingEngineBenchmark
{
    public const string SqliteDatabasePath = "../../../../../../../../Data/sz.000400-sz.000499_1d_baostock.db";
    SqliteHelper sqliteHelper;

    public BackTestingEngineBenchmark()
    {
        sqliteHelper = new();
        sqliteHelper.Open(SqliteDatabasePath);
    }

    [Benchmark(Baseline = true)]
    public int UseDataTable()
    {
        DataTable dt = sqliteHelper.QueryAsDataTable("SELECT * FROM 'sz.000400';");
        return dt.Rows.Count;
    }

    [Benchmark]
    public int voidUseDictionaryList()
    {
        IEnumerable<IDictionary<string,object>> rows = sqliteHelper.QueryAsDictionaryList("SELECT * FROM 'sz.000400';");
        return rows.Count();
    }
}