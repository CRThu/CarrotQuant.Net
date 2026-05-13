using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    public interface IDataStorageBuilder
    {
        void AddFrame(string symbol, string time, StockFrame frame);
        IDataStorage Build();
    }
}
