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
            Browser.Address = Directory.GetCurrentDirectory() + "/View/Chart1.html";
        }

        DateTime dateTime = DateTime.Parse("2020-01-01");
        CandleData cd = new();
        int trend = 10;
        Random randomNum = new Random(Guid.NewGuid().GetHashCode());
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 100; i++)
            {
                dateTime = dateTime.AddDays(1);
                cd.datetime.Add(dateTime.ToString("yyyy-MM-dd"));
                trend += randomNum.Next(-3, 3+1);
                cd.open.Add(trend);
                cd.close.Add(trend + randomNum.Next(-5, 5+1));
                cd.high.Add(trend + 5 + randomNum.Next(0, 1+1));
                cd.low.Add(trend - 5 - randomNum.Next(0, 1+1));
            }
            var jsons = JsonSerializer.Serialize(cd);
            string js = $"InitCodeData({jsons});";
            Browser.GetBrowser().MainFrame.ExecuteJavaScriptAsync(js);
        }
    }
}
