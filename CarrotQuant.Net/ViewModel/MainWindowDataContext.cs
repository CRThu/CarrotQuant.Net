using CarrotBacktesting.NET.DataFeed;
using CarrotQuant.Net.Model;
using CarrotQuant.Net.Model.Common;
using CarrotQuant.Net.Model.EChartsData;
using CarrotQuant.Net.Model.IO;
using CarrotQuant.Net.Model.Utility;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CarrotQuant.Net.ViewModel
{
    public class MainWindowDataContext : NotificationObject
    {
        // Dialog管理器
        private IDialogCoordinator dialogCoordinator;

        // 数据库
        private SqliteHelper dataBase = new();
        public SqliteHelper DataBase
        {
            get => dataBase;
            set
            {
                dataBase = value;
                RaisePropertyChanged(() => DataBase);
            }
        }

        // 选中的表名(股票代码名)
        private string selectedDataBaseTableName;
        public string SelectedDataBaseTableName
        {
            get => selectedDataBaseTableName;
            set
            {
                selectedDataBaseTableName = value;
                RaisePropertyChanged(() => SelectedDataBaseTableName);
            }
        }

        // 选中的股票代码数据表
        private DataTable selectedDataBaseTable;
        public DataTable SelectedDataBaseTable
        {
            get => selectedDataBaseTable;
            set
            {
                selectedDataBaseTable = value;
                RaisePropertyChanged(() => SelectedDataBaseTable);
            }
        }

        // ECharts Data
        private EChartsData chartData = new();
        public EChartsData ChartData
        {
            get => chartData;
            set
            {
                chartData = value;
                RaisePropertyChanged(() => ChartData);
            }
        }

        public MainWindowDataContext()
        {

        }

        public MainWindowDataContext(IDialogCoordinator instance)
        {
            dialogCoordinator = instance;
        }

        // 函数
        public void LoadDataBase()
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Filter = "SQLite数据库文件|*.db;*.sqlite"
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    DataBase.Open(openFileDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                MahAppsDialogUtility.ShowExceptionMessage(this, dialogCoordinator, ex);
            }
        }

        public void UpdateDataBaseSelectedTable()
        {
            try
            {
                SelectedDataBaseTable = DataBase.GetTable(SelectedDataBaseTableName);
                ShareData shareData = new(SelectedDataBaseTable, "交易日期", new string[] { "开盘价", "最低价" });
                DataFeed dataFeed = new();
                dataFeed.SetShareData(SelectedDataBaseTable.TableName, shareData);
            }
            catch (Exception ex)
            {
                MahAppsDialogUtility.ShowExceptionMessage(this, dialogCoordinator, ex);
            }
        }

        public void ButtonClick()
        {

            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start(); //  开始监视代码运行时间

                EChartsData eChartsData = new("股票代码", "SH888888", 3);

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

                eChartsData.Series[1].MarkPoints = new List<object>();
                eChartsData.Series[1].MarkPoints.Add(new Dictionary<string, object>()
                {
                    {"coord",new List<dynamic>(){ "14:00", 3} },
                    {"value","B" }
                });
                eChartsData.Series[1].MarkPoints.Add(new Dictionary<string, object>()
                {
                    {"coord",new List<dynamic>(){ 3, 7} },
                    {"value","S" }
                });

                ChartData = eChartsData;
                stopwatch.Stop(); //  停止监视
                Debug.WriteLine($"AddTick:{stopwatch.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        // 事件
        private CommandBase loadDataBaseEvent;
        public CommandBase LoadDataBaseEvent
        {
            get
            {
                if (loadDataBaseEvent == null)
                {
                    loadDataBaseEvent = new CommandBase(new Action<object>(o =>
                    {
                        LoadDataBase();
                    }));
                }
                return loadDataBaseEvent;
            }
        }

        private CommandBase updateDataBaseSelectedTableEvent;
        public CommandBase UpdateDataBaseSelectedTableEvent
        {
            get
            {
                if (updateDataBaseSelectedTableEvent == null)
                {
                    updateDataBaseSelectedTableEvent = new CommandBase(new Action<object>(o =>
                    {
                        UpdateDataBaseSelectedTable();
                    }));
                }
                return updateDataBaseSelectedTableEvent;
            }
        }


        private CommandBase buttonClickEvent;
        public CommandBase ButtonClickEvent
        {
            get
            {
                if (buttonClickEvent == null)
                {
                    buttonClickEvent = new CommandBase(new Action<object>(o =>
                    {
                        ButtonClick();
                    }));
                }
                return buttonClickEvent;
            }
        }

    }
}
