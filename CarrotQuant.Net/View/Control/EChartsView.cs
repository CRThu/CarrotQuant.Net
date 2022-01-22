using CarrotQuant.Net.Model.EChartsData;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CarrotQuant.Net.View.Control
{
    public class EChartsView : WebView2
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Data", typeof(EChartsData), typeof(EChartsView), new PropertyMetadata(null, DataChangedCallback));

        public EChartsData Data
        {
            set
            {
                SetValue(TextProperty, value);
            }
            get
            {
                return (EChartsData)GetValue(TextProperty);
            }
        }
        private static void DataChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((EChartsView)d).DataChangedCallback(e.NewValue as EChartsData, e.OldValue as EChartsData);
        }

        private void DataChangedCallback(EChartsData newvalue, EChartsData oldvalue)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start(); //  开始监视代码运行时间

            string jsons = newvalue.ToJson();
            string js = $"UpdateData({jsons});";

            stopwatch.Stop(); //  停止监视
            Debug.WriteLine($"Serialize:{stopwatch.ElapsedMilliseconds}ms.");

            if (CoreWebView2 != null)
                this.CoreWebView2.ExecuteScriptAsync(js);
        }

        public EChartsView() : base()
        {
            //this.Source = new Uri("https://html5test.com");
            this.Source = new Uri(Directory.GetCurrentDirectory() + "/View/Chart1.html");
        }
    }
}
