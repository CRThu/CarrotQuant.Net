using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;
using System.IO;
using System.Text.Json;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    [TestClass()]
    public class ShareFrameMapperTests
    {
        [TestMethod()]
        public void DeserializeTest()
        {
            string filename = "mapper.baostock.csv.daliy.json";
            string mapperJsonpath = Path.Combine(UnitTestDirectory.MapperDirectory, filename);

            ShareFrameMapper sfm = ShareFrameMapper.Deserialize(mapperJsonpath)!;

            Assert.AreEqual("DateTime", sfm.MapDict["date"]);
            Assert.AreEqual("Amount", sfm.MapDict["amount"]);
            Assert.AreEqual("System.Double", sfm.TypeDict["amount"]);
        }
    }
}