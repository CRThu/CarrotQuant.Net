using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Utility
{
    public static class MiscExtensions
    {
        public static IEnumerable<T> GetDatatableColumn<T>(DataTable dt, string colName)
        {
            return dt.AsEnumerable().Select(r => r.Field<T>(colName));
        }
    }
}
