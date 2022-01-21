using CarrotQuant.Net.Model.EChartsData;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CarrotQuant.Net.View.Control
{
    public class EChartsView : WebView2
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Data", typeof(string), typeof(EChartsView), new PropertyMetadata(null, DataChangedCallback));

        public string Data
        {
            set
            {
                SetValue(TextProperty, value);
            }
            get
            {
                return (string)GetValue(TextProperty);
            }
        }
        private static void DataChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((EChartsView)d).DataChangedCallback(e.NewValue as string, e.OldValue as string);
        }

        private void DataChangedCallback(string newvalue, string oldvalue)
        {
            // todo
        }

        public EChartsView() : base()
        {
            //this.Source = new Uri("https://html5test.com");
            this.Source = new Uri(Directory.GetCurrentDirectory() + "/View/Chart1.html");
        }
    }
}
