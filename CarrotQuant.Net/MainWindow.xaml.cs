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
        EChartsData cd = new(new[] { 5, 10 }, new[] { 0, 0 }, 3);
        int trend = 100;
        Random randomNum = new Random(Guid.NewGuid().GetHashCode());
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Stopwatch stopwatch = new Stopwatch();
            cd.StockName = "证券名称";
            cd.StockCode = "SH888888";
            stopwatch.Start(); //  开始监视代码运行时间
            for (int i = 0; i < 50; i++)
            {
                dateTime = dateTime.AddDays(1);
                cd.AddTick(dateTime.ToString("yyyy-MM-dd"),
                    trend,
                    trend + randomNum.Next(0, 21 + 1),
                    trend - randomNum.Next(0, 21 + 1),
                    trend + randomNum.Next(-9, 9 + 1),
                    randomNum.Next(0, 10000)
                    );
                trend += randomNum.Next(-15, 15 + 1);
            }
            cd.calcTA();
            stopwatch.Stop(); //  停止监视
            Debug.WriteLine($"AddTick:{stopwatch.ElapsedMilliseconds}ms.");
            stopwatch.Reset();
            stopwatch.Start(); //  开始监视代码运行时间
            string jsons = cd.ToJson();
            stopwatch.Stop(); //  停止监视
            Debug.WriteLine($"Serialize:{stopwatch.ElapsedMilliseconds}ms.");
            string js = $"UpdateData({jsons});";
            Browser.CoreWebView2.ExecuteScriptAsync(js);
        }
    }
}
