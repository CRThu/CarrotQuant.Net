using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace CarrotQuant.Net.Model
{
    public class SqliteHelper
    {
        public string FileName;
        public SqliteHelper()
        {
        }

        public SqliteHelper(string fileName) : this()
        {
            FileName = fileName;
        }

        public DataTable Query(string query)
        {
            using IDbConnection connection = new SqliteConnection(
                new SqliteConnectionStringBuilder() { DataSource = FileName }.ToString());
            DataTable table = new();
            table.Load(connection.ExecuteReader(query));
            return table;
        }
    }
}
