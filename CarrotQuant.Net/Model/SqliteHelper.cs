using CarrotQuant.Net.Utility;
using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace CarrotQuant.Net.Model
{
    public class SqliteHelper : NotificationObject
    {
        private IDbConnection connection;

        public string fileName;
        public string FileName
        {

            get => fileName;
            set
            {
                fileName = value;
                RaisePropertyChanged(() => FileName);
            }
        }

        public List<string> tableNames;
        public List<string> TableNames
        {

            get => tableNames;
            set
            {
                tableNames = value;
                RaisePropertyChanged(() => TableNames);
            }
        }

        public Dictionary<string, dynamic> info;
        public Dictionary<string, dynamic> Info
        {

            get => info;
            set
            {
                info = value;
                RaisePropertyChanged(() => Info);
            }
        }

        public SqliteHelper()
        {
        }

        public SqliteHelper(string fileName) : this()
        {
            FileName = fileName;
        }

        public void Open(string fileName)
        {
            FileName = fileName;
            Open();
        }

        public void Open()
        {
            connection = new SqliteConnection(
               new SqliteConnectionStringBuilder() { DataSource = FileName }.ToString()
               );

            // 更新数据库基础信息
            TableNames = GetTableNames();
            Info = new()
            {
                { "文件路径", Path.GetDirectoryName(FileName) },
                { "文件名", Path.GetFileName(FileName) },
                { "数据表数量", TableNames.Count }
            };
        }

        public DataTable Query(string query)
        {
            DataTable table = new();
            table.Load(connection.ExecuteReader(query));
            return table;
        }

        public List<string> GetTableNames()
        {
            DataTable tbs = Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
            return MiscExtensions.GetDatatableColumn<string>(tbs, "name").ToList();
        }

        public DataTable GetTable(string tableName)
        {
            return Query($"SELECT * FROM '{tableName}'");
        }
    }
}
