using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;

namespace CarrotBacktesting.NET.Common
{
    public class SqliteHelper
    {
        private IDbConnection connection;
        public string FileName { get; set; }

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
        }

        public DataTable Query(string query)
        {
            DataTable table = new();
            table.Load(connection.ExecuteReader(query));
            return table;
        }

        public IEnumerable<string> GetTableNames()
        {
            DataTable tbs = Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
            return DataTableMisc.GetColumn<string>(tbs, "name");
        }

        public DataTable GetTable(string tableName)
        {
            return Query($"SELECT * FROM '{tableName}'");
        }
    }
}
