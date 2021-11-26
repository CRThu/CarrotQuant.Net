using CarrotQuant.Net.Model;
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
        private SqliteHelper sqliteHelper = new();

        private DataTable dg;
        public DataTable DG
        {
            get => dg;
            set
            {
                dg = value;
                RaisePropertyChanged(() => DG);
            }
        }

        private List<string> albums = new()
        {
            "sh.000111","sh.000333","sh.000334","sh.000335","sz.000444"
        };
        public List<string> Albums
        {
            get => albums;
            set
            {
                albums = value;
                RaisePropertyChanged(() => Albums);
            }
        }

        private CommandBase loadDatabaseEvent;
        public CommandBase LoadDatabaseEvent
        {
            get
            {
                if (loadDatabaseEvent == null)
                {
                    loadDatabaseEvent = new CommandBase(new Action<object>(o =>
                    {
                        OpenFileDialog openFileDialog = new()
                        {
                            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                            Filter = "SQLite数据库文件|*.db;*.sqlite"
                        };
                        if (openFileDialog.ShowDialog() == true)
                        {
                            sqliteHelper.FileName = openFileDialog.FileName;
                            DataTable dt = sqliteHelper.Query("select * from 'sh.601020'");
                            DG = dt;
                        }
                    }));
                }
                return loadDatabaseEvent;
            }
        }
    }
}
