using CarrotQuant.Net.Model;
using CarrotQuant.Net.ViewModel;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CarrotQuant.Net
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            DataContext = new MainWindowDataContext(DialogCoordinator.Instance);
            //Browser.Source = new Uri("https://html5test.com");
            Browser.Source = new Uri(Directory.GetCurrentDirectory() + "/View/Chart1.html");
        }

        DateTime dateTime = DateTime.Parse("2020-01-01");
        EChartsDataOld cd = new(new[] { 5, 10 }, new[] { 0, 0 }, 3);
        int trend = 100;
        Random randomNum = new Random(Guid.NewGuid().GetHashCode());
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Model.EChartsData.EChartsData eChartsData = new Model.EChartsData.EChartsData
            {
                StockName = "股票代码",
                StockCode = "SH888888",
                GridsCount = 3
            };

            Model.EChartsData.EChartsSeries eChartsSeries = new()
            {
                Name = "open",
                Type = "line",
                GridIndex = 0,
                DataXColumnsName = new() { "time" },
                DataYColumnsName = new() { "open" }
            };
            eChartsData.Series.Add(eChartsSeries);

            eChartsSeries = new()
            {
                Name = "kline",
                Type = "candlestick",
                GridIndex = 0,
                DataXColumnsName = new() { "time" },
                DataYColumnsName = new() { "open", "close", "high", "low" }
            };
            eChartsData.Series.Add(eChartsSeries);
            eChartsSeries = new()
            {
                Name = "close",
                Type = "line",
                GridIndex = 1,
                DataXColumnsName = new() { "time" },
                DataYColumnsName = new() { "close" }
            };
            eChartsData.Series.Add(eChartsSeries);
            eChartsSeries = new()
            {
                Name = "MA5",
                Type = "line",
                GridIndex = 1,
                DataXColumnsName = new() { "time" },
                DataYColumnsName = new() { "MA5" }
            };
            eChartsData.Series.Add(eChartsSeries);
            eChartsSeries = new()
            {
                Name = "Vol",
                Type = "bar",
                GridIndex = 2,
                DataXColumnsName = new() { "time" },
                DataYColumnsName = new() { "volume" }
            };
            eChartsData.Series.Add(eChartsSeries);

            eChartsData.Dimension.Add("time");
            eChartsData.Data.Add(new string[] { "00:00", "01:00", "02:00", "03:00", "04:00", "05:00", "06:00", "07:00", "08:00", "09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00", "17:00", "18:00" });
            eChartsData.Dimension.Add("open");
            eChartsData.Data.Add(new dynamic[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
            eChartsData.Dimension.Add("close");
            eChartsData.Data.Add(new dynamic[] { 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5, 9.5, 10.5, 9.5, 8.5, 7.5, 6.5, 5.5, 4.5, 3.5, 2.5, 1.5 });
            eChartsData.Dimension.Add("high");
            eChartsData.Data.Add(new dynamic[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3 });
            eChartsData.Dimension.Add("low");
            eChartsData.Data.Add(new dynamic[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 });
            eChartsData.Dimension.Add("volume");
            eChartsData.Data.Add(new dynamic[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 });
            eChartsData.Dimension.Add("MA5");
            double[] real = new double[19];
            int idx, cnt;
            double[] real1 = new double[19];
            TicTacTec.TA.Library.Core.MovingAverage(0, eChartsData.Data[1].Length - 1, eChartsData.Data[1].Select(d => (double)d).ToArray(), 5, TicTacTec.TA.Library.Core.MAType.Sma, out idx, out cnt, real);
            Array.Copy(real, 0, real1, idx, cnt);
            eChartsData.Data.Add(real1.Select(d => (dynamic)d).ToArray());

            string jsons = eChartsData.ToJson();
            Debug.WriteLine(jsons);

            //Stopwatch stopwatch = new Stopwatch();
            //cd.StockName = "证券名称";
            //cd.StockCode = "SH888888";
            //stopwatch.Start(); //  开始监视代码运行时间
            //for (int i = 0; i < 50; i++)
            //{
            //    dateTime = dateTime.AddDays(1);
            //    cd.AddTick(dateTime.ToString("yyyy-MM-dd"),
            //        trend,
            //        trend + randomNum.Next(0, 21 + 1),
            //        trend - randomNum.Next(0, 21 + 1),
            //        trend + randomNum.Next(-9, 9 + 1),
            //        randomNum.Next(0, 10000)
            //        );
            //    trend += randomNum.Next(-15, 15 + 1);
            //}
            //cd.calcTA();
            //stopwatch.Stop(); //  停止监视
            //Debug.WriteLine($"AddTick:{stopwatch.ElapsedMilliseconds}ms.");
            //stopwatch.Reset();
            //stopwatch.Start(); //  开始监视代码运行时间
            //string jsons = cd.ToJson();
            //stopwatch.Stop(); //  停止监视
            //Debug.WriteLine($"Serialize:{stopwatch.ElapsedMilliseconds}ms.");
            string js = $"UpdateData({jsons});";
            Browser.CoreWebView2.ExecuteScriptAsync(js);
        }
    }
}
