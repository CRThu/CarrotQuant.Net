using CarrotBacktesting.Net.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
{
    /// <summary>
    /// 通过Sqlite数据库导入数据
    /// </summary>
    public class SqliteDataFeed : DataFeed
    {
        /// <summary>
        /// 数据库连接实例
        /// </summary>
        private SqliteHelper sql { get; set; } = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="fileName"></param>
        public SqliteDataFeed(string fileName) : base()
        {
            sql.Open(fileName);
        }

        /// <summary>
        /// 通过数据库导入单支股票行情
        /// </summary>
        /// <param name="shareCode"></param>
        /// <param name="timeColName"></param>
        /// <param name="dataColNames"></param>
        /// <param name="stringColNames"></param>
        public void SetShareData(string shareCode, string timeColName, string[] dataColNames, string[] stringColNames = null)
        {

            ShareData shareData = new();
            List<string> colNames = new();
            colNames.Add(timeColName);
            colNames.AddRange(dataColNames);
            if (stringColNames != null)
                colNames.AddRange(stringColNames);
            DataTable dt = sql.GetTable(shareCode, colNames.ToArray());
            shareData.DataTable2ShareData(dt, timeColName, dataColNames, stringColNames);

            SetShareData(shareCode, shareData);
        }
    }
}
