using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class DataTableMisc
    {
        public static IEnumerable<T> GetColumn<T>(DataTable dataTable, string columnName)
        {
            return dataTable.AsEnumerable().Select(r => r.Field<T>(columnName))!;
        }
    }
}
