using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using Sylvan;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.Net.Storage
{
    public class CsvReader
    {
        public SimulationOptions Options { get; set; }

        public string[] ColumnsName => Options.Fields;

        public CsvReader(SimulationOptions options)
        {
            Options = options;
        }

        public IEnumerable<ShareFrame> Load(string path)
        {
            List<ShareFrame> frames = new();

            var pool = new StringPool();
            var opts = new CsvDataReaderOptions {
                StringFactory = pool.GetString,
                BufferSize = 1048576
            };
            var csv = CsvDataReader.Create(path, opts);

            int[] columnsIndex = new int[ColumnsName.Length];
            string[] columnsMappedName = new string[ColumnsName.Length];
            string[] columnsValue = new string[ColumnsName.Length];
            for (int i = 0; i < ColumnsName.Length; i++)
            {
                columnsIndex[i] = csv.GetOrdinal(ColumnsName[i]);
                columnsMappedName[i] = Options.Mapper!.MapDict[ColumnsName[i]];
            }

            while (csv.Read())
            {
                for (int i = 0; i < columnsIndex.Length; i++)
                {
                    // string pool
                    columnsValue[i] = csv.GetString(columnsIndex[i]);
                }
                // add frames
                frames.Add(new(columnsMappedName, columnsValue, "TEST.CODE"));
            }

            return frames;
        }
    }
}
