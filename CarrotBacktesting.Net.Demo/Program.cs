using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;
using static CarrotBacktesting.Net.Common.Enums;


namespace CarrotBacktesting.Net.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(Json.Serialize(new string[] { "a", "vv" }));
            Console.WriteLine("Hello, World!");

            var options = new SimulationOptions() {
                DataFeedSource = DataFeedSource.Sqlite,
                DataFeedPath = @"D:\Projects\CarrotQuant.Net\Data\sz.000400-sz.000499_1d_baostock.db",
                SimulationStartTime = new DateTime(2021, 10, 1),
                SimulationEndTime = new DateTime(2021, 11, 1),
                //SimulationDuration = new TimeSpan(0, 0, 1),
                //ShareNames = new[] { "sz.000422" },
                StockCodes = new[] { "sz.000422", "sz.000423" },
                //IsEnableShareStatusFlag = true,
                Fields = new string[] { "交易日期", "开盘价", "最高价", "最低价", "收盘价", "成交量", "是否ST", "交易状态", "滚动市盈率" }
            }.Load();

            IEngine engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            engine.Run();
            //engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            //engine.Run();
            //engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            //engine.Run();

#if RELEASE
            options = new SimulationOptions()
            {
                //IsSqliteDataFeed = true,
                DataFeedPath = @"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db",
                SimulationStartTime = new DateTime(2021, 6, 1),
                SimulationEndTime = new DateTime(2021, 11, 1),
                //SimulationDuration = new TimeSpan(0, 0, 1),
                ShareNames = new[] { "sz.000422" },
            }.Load();

            engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            engine.Run();
            engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            engine.Run();
            engine = BackTestingEngineFactory.Create(new BasicStrategy(), options);
            engine.Run();
#endif
        }
    }
}