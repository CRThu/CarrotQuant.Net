using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using Sylvan.Data.Csv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CarrotBacktesting.Net.Storage
{
    /// <summary>
    /// Csv操作类
    /// </summary>
    public class CsvHelper
    {
        /// <summary>
        /// 字段映射表
        /// </summary>
        private ShareFrameMapper? Mapper { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mapper"></param>
        public CsvHelper(ShareFrameMapper? mapper = null)
        {
            Mapper = mapper;
        }

        /// <summary>
        /// 读取历史数据
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="fields">字段集合</param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <returns>ShareFrame[]</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ShareFrame[] Read(string fileName, string stockCode, string[] fields, DateTime? startTime = null, DateTime? endTime = null)
        {

            var csv = CsvDataReader.Create(fileName);

            //var idIndex = csv.GetOrdinal("Id");
            //var nameIndex = csv.GetOrdinal("Name");
            //var dateIndex = csv.GetOrdinal("Date");

            string[] fieldsMapName = new string[fields.Length];
            int[] fieldsIndex = new int[fields.Length];
            string[] fieldsStr = new string[fields.Length];
            List<ShareFrame> frames = new();

            for (int i = 0; i < fields.Length; i++)
            {
                fieldsIndex[i] = csv.GetOrdinal(fields[i]);
            }

            // 映射
            string[] types = new string[fields.Length];
            if (Mapper != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    fieldsMapName[i] = Mapper[fields[i]];
                    if (Mapper.TypeDict.TryGetValue(fieldsMapName[i], out string? type))
                        types[i] = type;
                }
            }

            while (csv.Read())
            {
                ShareFrame newFrame = new ShareFrame();
                for (int i = 0; i < fieldsIndex.Length; i++)
                {
                    fieldsStr[i] = csv.GetString(fieldsIndex[i]);
                    switch (fieldsMapName[i])
                    {
                        case "Code":
                            newFrame.Code = csv.GetString(fieldsIndex[i]);
                            break;
                        case "Time":
                            newFrame.Time = DynamicConverter.GetValue(csv.GetString(fieldsIndex[i]), "System.DateTime");
                            break;
                        case "Open":
                            newFrame.Open = csv.GetDouble(fieldsIndex[i]);
                            break;
                        case "High":
                            newFrame.High = csv.GetDouble(fieldsIndex[i]);
                            break;
                        case "Low":
                            newFrame.Low = csv.GetDouble(fieldsIndex[i]);
                            break;
                        case "Close":
                            newFrame.Close = csv.GetDouble(fieldsIndex[i]);
                            break;
                        case "Volume":
                            newFrame.Volume = csv.GetDouble(fieldsIndex[i]);
                            break;
                        case "Status":
                            newFrame.Volume = DynamicConverter.GetValue(csv.GetString(fieldsIndex[i]), "System.DateTime");
                            break;
                        default:
                            newFrame.Params ??= new();
                            if (types == null || (types != null && types[i] == null))
                            {
                                newFrame.Params[fieldsMapName[i]] = fieldsIndex[i];
                            }
                            else
                            {
                                newFrame.Params[fieldsMapName[i]] = DynamicConverter.GetValue(csv.GetString(fieldsIndex[i]), types![i]!);
                            }
                            break;
                    }
                }

                if (newFrame.Code == null)
                {
                    if (stockCode != null)
                    {
                        newFrame.Code = stockCode;
                    }
                    else
                    {
                        throw new NotImplementedException("Code == null");
                    }
                }

                // TODO
                frames.Add(newFrame);
                //var id = csv.GetInt32(0);
                //var name = csv.GetString(1);
                //var date = csv.GetDateTime(2);
                //var id = csv.GetInt32(idIndex);
                //var name = csv.GetString(nameIndex);
                //var date = csv.GetDateTime(dateIndex);

            }
            return frames.ToArray();
        }
    }
}
