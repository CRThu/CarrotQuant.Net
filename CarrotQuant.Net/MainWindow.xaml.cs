using CarrotQuant.Net.Model;
using CarrotQuant.Net.Model.EChartsData;
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start(); //  开始监视代码运行时间

                EChartsData eChartsData = new("股票代码", "SH888888", 3);

                EChartsSeries eChartsSeries = new("open", EChartSeriesType.line, 0, "time", "open");
                eChartsData.AddSeries("open", EChartSeriesType.line, 0, "time", "open");
                eChartsData.AddSeries("kline", EChartSeriesType.candlestick, 0, "time", new[] { "open", "close", "high", "low" });
                eChartsData.AddSeries("close", EChartSeriesType.line, 1, "time", "close");
                eChartsData.AddSeries("MA5", EChartSeriesType.line, 1, "time", "MA5");
                eChartsData.AddSeries("Vol", EChartSeriesType.bar, 2, "time", "volume");

                eChartsData.AddData("time", new string[] { "00:00", "01:00", "02:00", "03:00", "04:00", "05:00", "06:00", "07:00", "08:00", "09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00", "17:00", "18:00" });
                eChartsData.AddData("open", new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
                eChartsData.AddData("close", new double[] { 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5, 9.5, 10.5, 9.5, 8.5, 7.5, 6.5, 5.5, 4.5, 3.5, 2.5, 1.5 });
                eChartsData.AddData("high", new dynamic[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3 });
                eChartsData.AddData("low", new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 });
                eChartsData.AddData("volume", new double[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 });

                eChartsData.AddData("MA5", TechnicalAnalysis.MovingAverage((double[])eChartsData.Data["close"], 5));

                stopwatch.Stop(); //  停止监视
                Debug.WriteLine($"AddTick:{stopwatch.ElapsedMilliseconds}ms.");
                stopwatch.Reset();
                stopwatch.Start(); //  开始监视代码运行时间

                string jsons = eChartsData.ToJson();
                Debug.WriteLine(jsons);

                stopwatch.Stop(); //  停止监视
                Debug.WriteLine($"Serialize:{stopwatch.ElapsedMilliseconds}ms.");
                string js = $"UpdateData({jsons});";
                Browser.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
    }
}
