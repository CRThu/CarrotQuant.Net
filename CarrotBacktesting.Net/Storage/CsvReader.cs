using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Engine;
using Sylvan;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.Net.Storage
{
    public class CsvReader
    {
        public SimulationOptions Options { get; set; }

        public string[] Columns => Options.Fields;
        public int[] ColumnsIndex { get; set; }

        public CsvReader(SimulationOptions options)
        {
            Options = options;
        }

        public void Load(string path)
        {
            var pool = new StringPool();
            var opts = new CsvDataReaderOptions {
                StringFactory = pool.GetString
            };
            var csv = CsvDataReader.Create(path, opts);

            ColumnsIndex = new int[Columns.Length];
            for (int i = 0; i < Columns.Length; i++)
            {
                ColumnsIndex[i] = csv.GetOrdinal(Columns[i]);
            }

            while (csv.Read())
            {
                for (int i = 0; i < ColumnsIndex.Length; i++)
                {
                    var name = csv.GetString(ColumnsIndex[i]);
                }
            }
        }
    }
}
