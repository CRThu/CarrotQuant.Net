using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Storage
{
    public class CsvDataProvider : IDataProvider
    {

        public CsvDataProvider()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ShareFrame> GetShareData(string stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ShareFrame> GetShareData(string[] stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {
            throw new NotImplementedException();
        }
    }
}
