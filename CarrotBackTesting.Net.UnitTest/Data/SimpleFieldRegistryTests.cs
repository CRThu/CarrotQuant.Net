using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class SimpleFieldRegistryTests
    {
        [TestMethod]
        public void TestRegisterAndGetField()
        {
            var registry = new SimpleFieldRegistry();
            registry.RegisterField("close", typeof(double));

            Assert.IsTrue(registry.FieldExists("close"));
            Assert.AreEqual(typeof(double), registry.GetFieldType("close"));
            
            var info = registry.GetFieldInfo("close");
            Assert.AreEqual("close", info.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void TestGetNonExistentFieldThrows()
        {
            var registry = new SimpleFieldRegistry();
            registry.GetFieldInfo("nonexistent");
        }
    }
}
