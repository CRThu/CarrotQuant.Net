using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{

    public enum DynamicDataType
    {
        Null,
        String,
        Double,
        Boolean
    };

    public class DynamicObject
    {
        public DynamicDataType DataType { get; set; }

        public string stringValue;
        public double doubleValue;
        public bool boolValue;

        public DynamicObject()
        {
            DataType = DynamicDataType.Null;
        }

        public void WriteDouble(double value)
        {
            DataType = DynamicDataType.Double;
            doubleValue = value;
        }

        public void WriteBoolean(bool value)
        {
            DataType = DynamicDataType.Boolean;
            boolValue = value;
        }

        public void WriteString(string value)
        {
            DataType = DynamicDataType.String;
            stringValue = value;
        }

        public double ReadDouble()
        {
            return doubleValue;
        }

        public bool ReadBoolean()
        {
            return boolValue;
        }

        public string ReadString()
        {
            return stringValue;
        }

        public dynamic Read()
        {
            if (DataType == DynamicDataType.Null)
                return null;
            else if (DataType == DynamicDataType.String)
                return ReadString();
            else if (DataType == DynamicDataType.Double)
                return ReadDouble();
            else if (DataType == DynamicDataType.Boolean)
                return ReadBoolean();
            else
                return null;
        }
    }


}
