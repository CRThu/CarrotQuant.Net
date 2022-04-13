using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using System.Diagnostics;

namespace CarrotBacktesting.Net.Common
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
#if DEBUG
            Console.WriteLine($"SqliteHelper.Query({query}) called.");
            Stopwatch sw = new();
            sw.Start();
#endif
            DataTable table = new();
            table.Load(connection.ExecuteReader(query));

#if DEBUG
            sw.Stop();
            Console.WriteLine($"Elapsed time: {sw.ElapsedMilliseconds} ms.");
#endif
            return table;
        }

        public IEnumerable<string> GetTableNames()
        {
            DataTable tbs = Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
            return DataTableMisc.GetColumn<string>(tbs, "name");
        }

        public DataTable GetTable(string tableName, string[]? columnNames = null)
        {
            string joinColumns = columnNames != null ? string.Join(',', columnNames) : "*";
            string queryCmd = $"SELECT {joinColumns} FROM '{tableName}'";

            DataTable dataTable = Query(queryCmd);

            return dataTable;
        }
    }
}
