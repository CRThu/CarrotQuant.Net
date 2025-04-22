using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Common;
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
        public Dictionary<string, string> MapperDict => Options.Mapper.MapDict;
        public (DateTime start, DateTime end) Filter => (Options.SimulationStartTime ?? DateTime.MinValue, Options.SimulationStartTime ?? DateTime.MaxValue);

        public CsvReader(SimulationOptions options)
        {
            Options = options;
        }

        public IEnumerable<ShareFrame> Load(string path, string symbol)
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
            int timeFilterIndex = 0;
            for (int i = 0; i < ColumnsName.Length; i++)
            {
                columnsIndex[i] = csv.GetOrdinal(ColumnsName[i]);
                columnsMappedName[i] = MapperDict[ColumnsName[i]];
                if (columnsMappedName[i] == "Time")
                {
                    timeFilterIndex = i;
                }
            }

            while (csv.Read())
            {
                var timeVal = csv.GetString(columnsIndex[timeFilterIndex]);
                var time = DynamicConverter.GetValue<DateTime>(timeVal);
                if (time >= Filter.start && time <= Filter.end)
                {
                    for (int i = 0; i < columnsIndex.Length; i++)
                    {
                        // string pool
                        columnsValue[i] = csv.GetString(columnsIndex[i]);
                    }
                    // add frames
                    frames.Add(new(columnsMappedName, columnsValue, symbol));
                }
            }

            return frames;
        }
    }
}
