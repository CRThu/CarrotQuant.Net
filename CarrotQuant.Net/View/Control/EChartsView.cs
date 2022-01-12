using CarrotQuant.Net.Model.EChartsData;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CarrotQuant.Net.View.Control
{
    public class EChartsView : WebView2
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Data", typeof(string), typeof(EChartsView));

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
    }
}
