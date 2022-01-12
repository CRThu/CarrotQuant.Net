using CarrotQuant.Net.Model;
using CarrotQuant.Net.Model.Common;
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
        private string eChartsData;
        public string EChartsData
        {
            get => eChartsData;
            set
            {
                eChartsData = value;
                RaisePropertyChanged(() => EChartsData);
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
            }
            catch (Exception ex)
            {
                MahAppsDialogUtility.ShowExceptionMessage(this, dialogCoordinator, ex);
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

    }
}
