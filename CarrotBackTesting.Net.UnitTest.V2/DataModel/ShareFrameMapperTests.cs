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
using CarrotBacktesting.Net.Common;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    [TestClass()]
    public class ShareFrameMapperTests
    {
        [TestMethod()]
        public void DeserializeTest()
        {
            string filename = "mapper.baostock.csv.daily.json";
            string mapperJsonpath = Path.Combine(UnitTestDirectory.JsonDirectory, filename);

            ShareFrameMapper sfm = Json.DeSerializeFromFile<ShareFrameMapper>(mapperJsonpath)!;
            sfm.UpdateGlobalBoolString();

            Assert.AreEqual("Time", sfm.MapDict["date"]);
            Assert.AreEqual("Amount", sfm.MapDict["amount"]);
            Assert.AreEqual("System.Double", sfm.TypeDict["amount"]);
            Assert.IsTrue(BooleanEx.TrueStrings.Contains("测试True"));
            Assert.IsTrue(BooleanEx.FalseStrings.Contains("测试False"));
        }
    }
}