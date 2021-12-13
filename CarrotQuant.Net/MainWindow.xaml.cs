using CarrotQuant.Net.Model;
using CarrotQuant.Net.ViewModel;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
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
        EChartsData cd = new(new[] { 5, 10 }, 3);
        int trend = 100;
        Random randomNum = new Random(Guid.NewGuid().GetHashCode());
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            cd.StockName = "证券名称";
            cd.StockCode = "SH888888";
            for (int i = 0; i < 15; i++)
            {
                dateTime = dateTime.AddDays(1);
                cd.Add(dateTime.ToString("yyyy-MM-dd"),
                    trend,
                    trend + randomNum.Next(0, 21 + 1),
                    trend - randomNum.Next(0, 21 + 1),
                    trend + randomNum.Next(-9, 9 + 1)
                    );
                trend += randomNum.Next(-15, 15 + 1);
            }
            string jsons = cd.ToJson();
            string js = $"UpdateData({jsons});";
            Browser.CoreWebView2.ExecuteScriptAsync(js);
        }
    }
}
