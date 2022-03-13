using CarrotBacktesting.NET.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class SqliteDataFeed : DataFeed
    {
        private SqliteHelper sql { get; set; } = new();

        public SqliteDataFeed(string fileName) : base()
        {
            sql.Open(fileName);
        }

        public void SetShareData(string shareCode, string timeColName, string[] dataColNames)
        {
            ShareData shareData = new();
            string[] colNames = new string[dataColNames.Length + 1];
            colNames[0] = timeColName;
            dataColNames.CopyTo(colNames, 1);
            DataTable dt = sql.GetTable(shareCode, colNames);
            shareData.DataTable2ShareData(dt, timeColName, dataColNames);

            SetShareData(shareCode, shareData);
        }

        public void SetAllShareData(string timeColName, string[] dataColNames)
        {
            foreach (var code in sql.GetTableNames())
                SetShareData(code, timeColName, dataColNames);
        }
    }
}
