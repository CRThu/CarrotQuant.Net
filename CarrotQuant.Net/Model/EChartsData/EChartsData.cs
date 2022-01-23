using CarrotQuant.Net.Model.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.EChartsData
{
    public class EChartsData : NotificationObject
    {
        // 图表标题(股票名称+股票代码)
        private string stockName;
        public string StockName
        {
            get => stockName;
            set
            {
                stockName = value;
                RaisePropertyChanged(() => StockName);
            }
        }

        private string stockCode;
        public string StockCode
        {
            get => stockCode;
            set
            {
                stockCode = value;
                RaisePropertyChanged(() => StockCode);
            }
        }

        // 主副图数量:主图,[+副图1,][+副图2]
        private int gridsCount;
        public int GridsCount
        {
            get => gridsCount;
            set
            {
                gridsCount = value;
                RaisePropertyChanged(() => GridsCount);
            }
        }

        // 图表集合
        private ObservableCollection<EChartsSeries> series = new();
        public ObservableCollection<EChartsSeries> Series
        {
            get => series;
            set
            {
                series = value;
                RaisePropertyChanged(() => Series);
            }
        }

        // 数据源集合
        private ObservableDictionary<string, object> data = new();
        public ObservableDictionary<string, object> Data
        {
            get => data;
            set
            {
                data = value;
                RaisePropertyChanged(() => Data);
            }
        }

        // 构造函数
        public EChartsData()
        {
            StockName = string.Empty;
            StockCode = string.Empty;
            GridsCount = 1;
            Series = new();
            Data = new();
        }

        public EChartsData(string stockName, string stockCode, int gridsCount = 1) : this()
        {
            StockName = stockName;
            StockCode = stockCode;
            GridsCount = gridsCount;
        }

        // 添加Series
        public void AddSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string dataYColumnName)
        {
            Series.Add(new EChartsSeries(name, type, gridIndex, dataXColumnName, dataYColumnName));
            RaisePropertyChanged(() => Series);
        }

        public void AddSeries(string name, EChartSeriesType type, int gridIndex, string dataXColumnName, string[] dataYColumnsName)
        {
            Series.Add(new EChartsSeries(name, type, gridIndex, dataXColumnName, dataYColumnsName));
            RaisePropertyChanged(() => Series);
        }

        // 添加Data数组
        public void AddData(string dimension, object data)
        {
            if (data.GetType().BaseType == typeof(Array))
            {
                Data.Add(dimension, data);
                RaisePropertyChanged(() => Data);
            }
            else
            {
                throw new ArrayTypeMismatchException("Argument \'data\' of AddData() BaseType is not Array");
            }
        }

        // 序列化Json
        public string ToJson()
        {
            var jsons = JsonSerializer.Serialize(this);
            return jsons;
        }
    }
}
