using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using System.Diagnostics;
using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Sqlite操作类
    /// </summary>
    public class SqliteHelper
    {
        public string FileName { get; set; }

        private IDbConnection Connection { get; set; }

        /// <summary>
        /// 字段映射表, 格式如下
        /// <code>
        /// new ShareFrameMapper()
        /// {
        ///     ["[index]"] = "[index]",
        ///     ["交易日期"] = "DateTime",
        ///     ["收盘价"] = "close"
        /// }
        ///  </code>
        /// </summary>
        private ShareFrameMapper? Mapper { get; set; }

        /// <summary>
        /// 打开Sqlite数据库
        /// </summary>
        /// <param name="fileName">数据库文件路径</param>
        /// <param name="mapper">字段映射信息存储类</param>
        public void Open(string fileName, ShareFrameMapper? mapper = null)
        {
            FileName = fileName;
            Connection = new SqliteConnection(
               new SqliteConnectionStringBuilder() { DataSource = FileName }.ToString()
               );
            Connection.Open();

            this.Mapper = mapper;
        }

        /// <summary>
        /// 查询语句, 返回首个T类型数据
        /// </summary>
        /// <typeparam name="T">返回数据类型</typeparam>
        /// <param name="query">查询语句</param>
        /// <returns>返回T类型查询结果</returns>
        public T Query<T>(string query)
        {
            //Console.WriteLine($"SqliteHelper.Query({query}) called.");
            return Connection.ExecuteScalar<T>(query);
        }

        /// <summary>
        /// 查询语句, 返回DataTable类型数据
        /// </summary>
        /// <param name="query">查询语句</param>
        /// <returns>返回DataTable类型查询结果</returns>
        public DataTable QueryAsDataTable(string query)
        {
            //Console.WriteLine($"SqliteHelper.QueryDataTable({query}) called.");
            DataTable table = new();
            table.Load(Connection.ExecuteReader(query));
            return table;
        }

        /// <summary>
        /// 查询语句, 返回多行字典数据
        /// </summary>
        /// <param name="query">查询语句</param>
        /// <returns>返回多行字典数据</returns>
        public IEnumerable<IDictionary<string, object>> QueryAsDictionaryList(string query)
        {
            return Connection.Query(query).Cast<IDictionary<string, object>>();
        }

        /// <summary>
        /// 查询数据库所有表名并返回
        /// </summary>
        /// <returns>返回数据库所有表名</returns>
        public IEnumerable<string> GetTableNames()
        {
            var rows = QueryAsDictionaryList("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
            return rows.Select(kv => (string)kv["name"]);
        }

        /// <summary>
        /// 查询数据表并返回
        /// TODO: 过滤器优化
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="columnNames">字段名数组</param>
        /// <param name="filter">过滤器, (过滤字段名, a, b, 过滤条件)</param>
        /// <returns>返回表查询结果</returns>
        public IEnumerable<IDictionary<string, object>> GetTable(string tableName, string[]? columnNames = null,
            (string columnName, string a, string b, FilterCondition fd)? filter = null)
        {

            string selectStatement;
            if (columnNames != null)
            {
                List<string> fields = new(columnNames);
                // SELECT 开盘价 as open,最高价 as high,最低价 as 最低价,收盘价 as close FROM 'sz.000400'
                // SELECT 交易日期 as DateTime,收盘价 as close FROM 'sz.000400' WHERE DateTime >= '2020-01-01' AND DateTime <= '2022-12-31';
                if (Mapper != null)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        fields[i] = fields[i] + " as " + Mapper[fields[i]];
                    }
                }
                selectStatement = string.Join(',', fields);
            }
            else
                selectStatement = "*";

            string whereStatement;
            if (filter != null)
            {
                whereStatement = " WHERE";
                whereStatement += $" {filter.Value.columnName} >= \'{filter.Value.a}\' AND {filter.Value.columnName} <= \'{filter.Value.b}\'";
            }
            else
                whereStatement = string.Empty;

            string queryCmd = $"SELECT {selectStatement} FROM '{tableName}'{whereStatement};";

            // Console.WriteLine($"SqliteHelper.QueryDataTable({queryCmd}) called.");
            var table = QueryAsDictionaryList(queryCmd);

            return table;
        }
    }

    /// <summary>
    /// SQL语句WHERE条件枚举
    /// </summary>
    public enum FilterCondition
    {
        /// <summary>
        /// X >= A AND X <= B
        /// </summary>
        BigEqualAndSmallEqual
    }
}
