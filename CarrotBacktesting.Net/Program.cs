// See https://aka.ms/new-console-template for more information
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;

Console.WriteLine("Hello, World!");

var options = new BackTestingSimulationOptions()
{
    IsSqliteDataFeed = true,
    SqliteDatabasePath = @"D:\Projects\CarrotQuant.Net\Data\sz.000400-sz.000499_1d_baostock.db",
    SimulationStartTime = new DateTime(2021, 10, 1),
    SimulationEndTime = new DateTime(2021, 11, 1),
    //SimulationDuration = new TimeSpan(0, 0, 1),
    //ShareNames = new[] { "sz.000422" },
    ShareNames = new[] { "sz.000422", "sz.000423" },
    IsEnableShareStatusFlag = true,
    AdditionalStringColumnNames = new string[] { "是否ST", "交易状态" },
    AdditionalFields = new string[] { "滚动市盈率" }
};

IEngine engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
//engine = new BackTestingEngine(new BasicStrategy(), options);
//engine.Run();
//engine = new BackTestingEngine(new BasicStrategy(), options);
//engine.Run();

#if RELEASE
options = new BackTestingSimulationOptions()
{
    IsSqliteDataFeed = true,
    SqliteDatabasePath = @"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db",
    SimulationStartDateTime = new DateTime(2021, 6, 1),
    SimulationEndDateTime = new DateTime(2021, 11, 1),
    SimulationDuration = new TimeSpan(0, 0, 1),
    ShareNames = new[] { "sz.000422" },
};

engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
engine = new BackTestingEngine(new BasicStrategy(), options);
engine.Run();
#endif