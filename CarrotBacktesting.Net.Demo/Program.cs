using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using Sylvan.Data.Csv;
using System.Data;
using System.Diagnostics;


namespace CarrotBacktesting.Net.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            Stopwatch loadStopwatch = new();
            Stopwatch runStopwatch = new();


            // string filename = "options.daily.no-adjust.json";
            // string filename = "options.daily.post-adjust.json";
            string filename = "options.5min.post-adjust.json";
            string optionsJsonPath = Path.Combine(UnitTestDirectory.AsBsJsonTestDataDir, filename);
            loadStopwatch.Start();
            BackTestingEngine engine = BackTestingEngine.Create(new DebugStrategy(), optionsJsonPath);
            loadStopwatch.Stop();
            runStopwatch.Start();
            engine.Run();
            runStopwatch.Stop();

            int klinesCount = engine.Simulator.KLineCount;
            int ticksCount = engine.Simulator.TickCount;
            double loadTicksSpeed = (double)ticksCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double loadKlinesSpeed = (double)klinesCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double runTicksSpeed = (double)ticksCount / runStopwatch.ElapsedMilliseconds * 1000;
            double runKlinesSpeed = (double)klinesCount / runStopwatch.ElapsedMilliseconds * 1000;

            Console.WriteLine($"回测已完成, 共测试 {ticksCount} Ticks, "
                + $"{klinesCount} KLines");
            Console.WriteLine($"加载耗时: {loadStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"加载速度: {loadTicksSpeed:F3} Ticks/Sec, "
                + $"{loadKlinesSpeed:F3} KLines/Sec");
            Console.WriteLine($"回测耗时: {runStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"回测速度: {runTicksSpeed:F3} Ticks/Sec, "
                + $"{runKlinesSpeed:F3} KLines/Sec");



            string fn = "C:\\Users\\Carrot\\Desktop\\sh.600000.csv";
            string[] fields = engine.Options.Fields!;
            Stopwatch stopwatch1 = new();
            stopwatch1.Start();

            CsvHelper csvHelper = new CsvHelper(engine.Options.Mapper);
            ShareFrame[] data = csvHelper.Read(fn, "sh.600000", fields);
            stopwatch1.Stop();

            double speed1 = (double)data.Length / stopwatch1.ElapsedMilliseconds * 1000;
            Console.WriteLine($"加载已完成, "
                + $"{data.Length} KLines");
            Console.WriteLine($"加载耗时: {stopwatch1.ElapsedMilliseconds / 1000.0} Sec, "
                + $"{speed1:F3} KLines/Sec");


            Stopwatch stopwatch2 = new();
            stopwatch2.Start();
            using var csv = CsvDataReader.Create(fn);
            //var idIndex = csv.GetOrdinal("Id");
            //var nameIndex = csv.GetOrdinal("Name");
            //var dateIndex = csv.GetOrdinal("Date");

            int[] fieldsIndex = new int[fields.Length];
            string[] fieldsStr = new string[fields.Length];
            List<ShareFrame> frames = new();
            for (int i = 0; i < fields.Length; i++)
            {
                fieldsIndex[i] = csv.GetOrdinal(fields[i]);
            }

            while (csv.Read())
            {
                for (int i = 0; i < fieldsIndex.Length; i++)
                {
                    fieldsStr[i] = csv.GetString(fieldsIndex[i]);
                }
                // TODO
                frames.Add(new ShareFrame(fields,fieldsStr));
                //var id = csv.GetInt32(0);
                //var name = csv.GetString(1);
                //var date = csv.GetDateTime(2);
                //var id = csv.GetInt32(idIndex);
                //var name = csv.GetString(nameIndex);
                //var date = csv.GetDateTime(dateIndex);
            }
            stopwatch2.Stop();

            double speed2 = (double)data.Length / stopwatch2.ElapsedMilliseconds * 1000;
            Console.WriteLine($"加载已完成, "
                + $"{data.Length} KLines");
            Console.WriteLine($"加载耗时: {stopwatch2.ElapsedMilliseconds / 1000.0} Sec, "
                + $"{speed2:F3} KLines/Sec");

        }

    }
}