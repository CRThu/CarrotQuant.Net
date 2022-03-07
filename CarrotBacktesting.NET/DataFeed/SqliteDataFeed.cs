using CarrotBacktesting.NET.Common;
using System;
using System.Collections.Generic;
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
        }

    }
}
