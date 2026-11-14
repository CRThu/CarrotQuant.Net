using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    public record MultiTypeEntity(
        string Name,
        double Price,
        float Weight,
        int Count,
        long Timestamp,
        bool IsActive,
        DateTime Date);

    public record OutOfOrderEntity(int Age, string Name, double Score);

    public record NullableTypeEntity(string Name, double? Score, int? Age, DateTime? Birthday);

    public record DecimalEntity(string Symbol, decimal Amount, decimal? CashBonus);

    public enum MarketSide { Buy, Sell }

    public record EnumEntity(string Name, MarketSide Side, MarketSide? OptionalSide);

    [TestClass]
    public class EntityMapperAndDataRowTests
    {
        [TestMethod]
        public void EntityMapper_CompiledFactory_ShouldMapAllTypesWithoutBoxing()
        {
            var columns = new List<string> { "name", "price", "weight", "count", "timestamp", "is_active", "date" };
            var ctor = EntityMapper.ResolveConstructor<MultiTypeEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<MultiTypeEntity>(ctor, columns);

            var rawValues = new string[]
            {
                "AAPL",
                "150.25",
                "0.75",
                "100",
                "1625097600000",
                "true",
                "2025-01-15"
            };

            var entity = factory(rawValues);

            Assert.IsNotNull(entity);
            Assert.AreEqual("AAPL", entity.Name);
            Assert.AreEqual(150.25, entity.Price, 1e-6);
            Assert.AreEqual(0.75f, entity.Weight, 1e-6);
            Assert.AreEqual(100, entity.Count);
            Assert.AreEqual(1625097600000L, entity.Timestamp);
            Assert.IsTrue(entity.IsActive);
            Assert.AreEqual(new DateTime(2025, 1, 15), entity.Date);
        }

        [TestMethod]
        public void EntityMapper_OutOfOrderColumns_ShouldMapCorrectlyByParameterNames()
        {
            // 物理列顺序: [score, name, age]
            var columns = new List<string> { "score", "name", "age" };
            // 实体构造函数顺序: (int Age, string Name, double Score)
            var ctor = EntityMapper.ResolveConstructor<OutOfOrderEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<OutOfOrderEntity>(ctor, columns);

            // 物理列对应的值: score="95.5", name="Tom", age="18"
            var rawValues = new string[] { "95.5", "Tom", "18" };
            var entity = factory(rawValues);

            Assert.IsNotNull(entity);
            Assert.AreEqual(18, entity.Age);
            Assert.AreEqual("Tom", entity.Name);
            Assert.AreEqual(95.5, entity.Score, 1e-6);
        }

        [TestMethod]
        public void EntityMapper_MissingColumnOrNoMatchingConstructor_ShouldThrow()
        {
            var mismatchColumns = new List<string> { "foo", "bar", "baz" };
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                EntityMapper.ResolveConstructor<OutOfOrderEntity>(mismatchColumns);
            });
        }

        [TestMethod]
        public void DataRow_TypeConversions_ShouldWorkSafely()
        {
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "str_col", 0 },
                { "double_col", 1 },
                { "float_col", 2 },
                { "int_col", 3 },
                { "long_col", 4 },
                { "bool_col", 5 },
                { "date_col", 6 }
            };
            var values = new string[]
            {
                "hello",
                "123.456",
                "3.14",
                "42",
                "9876543210",
                "true",
                "2025-10-01"
            };

            var row = new DataRow(values, ordinals);

            Assert.AreEqual(7, row.Count);
            Assert.AreEqual(7, row.Columns.Count);
            Assert.AreEqual("hello", row[0]);
            Assert.AreEqual("hello", row["str_col"]);
            Assert.AreEqual(123.456, row.GetDouble("double_col"), 1e-6);
            Assert.AreEqual(3.14f, row.GetFloat("float_col"), 1e-6);
            Assert.AreEqual(42, row.GetInt("int_col"));
            Assert.AreEqual(9876543210L, row.GetLong("long_col"));
            Assert.IsTrue(row.GetBoolean("bool_col"));
            Assert.AreEqual(new DateTime(2025, 10, 1), row.GetDateTime("date_col"));

            // 属性检查与 TryGetValue
            Assert.IsTrue(row.ContainsColumn("str_col"));
            Assert.IsFalse(row.ContainsColumn("unknown_col"));
            Assert.IsTrue(row.TryGetValue("str_col", out var strVal));
            Assert.AreEqual("hello", strVal);
            Assert.IsFalse(row.TryGetValue("unknown_col", out _));

            // 缺省与非法容错
            Assert.AreEqual(0.0, row.GetDouble("non_existent"));
            Assert.AreEqual(0.0f, row.GetFloat("non_existent"));
            Assert.AreEqual(99, row.GetInt("non_existent", 99));
            Assert.AreEqual(888L, row.GetLong("non_existent", 888L));
            Assert.AreEqual("fallback", row.GetString("non_existent", "fallback"));
            Assert.IsFalse(row.GetBoolean("non_existent"));
            Assert.AreEqual(default(DateTime), row.GetDateTime("non_existent"));

            // 越界键抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                _ = row["missing_col"];
            });

            // DataRow 主键与时间戳自包含检查
            var fieldRow = new DataRow("sh.600000", values, ordinals);
            Assert.AreEqual("sh.600000", fieldRow.Key);
            Assert.IsNull(fieldRow.Timestamp);

            var eventRow = new DataRow("sh.600000", new DateTime(2025, 1, 1), values, ordinals);
            Assert.AreEqual("sh.600000", eventRow.Key);
            Assert.AreEqual(new DateTime(2025, 1, 1), eventRow.Timestamp);
        }

        [TestMethod]
        public void EntityMapper_NullableTypes_ShouldMapCorrectly()
        {
            var columns = new List<string> { "name", "score", "age", "birthday" };
            var ctor = EntityMapper.ResolveConstructor<NullableTypeEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<NullableTypeEntity>(ctor, columns);

            // 1. 全部字段非空
            var rawWithValues = new string[] { "Alice", "98.5", "25", "2000-05-20" };
            var entity1 = factory(rawWithValues);

            Assert.AreEqual("Alice", entity1.Name);
            Assert.AreEqual(98.5, entity1.Score!.Value, 1e-6);
            Assert.AreEqual(25, entity1.Age!.Value);
            Assert.AreEqual(new DateTime(2000, 5, 20), entity1.Birthday!.Value);

            // 2. 可空字段包含空字符串与空白字符
            var rawWithNulls = new string[] { "Bob", "", "  ", "" };
            var entity2 = factory(rawWithNulls);

            Assert.AreEqual("Bob", entity2.Name);
            Assert.IsNull(entity2.Score);
            Assert.IsNull(entity2.Age);
            Assert.IsNull(entity2.Birthday);
        }

        [TestMethod]
        public void DataRow_UnixTimestampParsing_ShouldWork()
        {
            var ordinals = new Dictionary<string, int>
            {
                { "ms_ts", 0 },
                { "iso_date", 1 },
                { "invalid_ts", 2 }
            };
            // 1625097600000 ms -> 2021-07-01 00:00:00 UTC
            var values = new string[] { "1625097600000", "2021-07-01", "not_a_number" };
            var row = new DataRow(values, ordinals);

            var dtMs = row.GetDateTime("ms_ts");
            var dtIso = row.GetDateTime("iso_date");
            var dtInvalid = row.GetDateTime("invalid_ts", new DateTime(1999, 1, 1));

            Assert.AreEqual(2021, dtMs.Year);
            Assert.AreEqual(7, dtMs.Month);
            Assert.AreEqual(1, dtMs.Day);

            Assert.AreEqual(2021, dtIso.Year);
            Assert.AreEqual(7, dtIso.Month);
            Assert.AreEqual(1, dtIso.Day);

            Assert.AreEqual(new DateTime(1999, 1, 1), dtInvalid);
        }

        [TestMethod]
        public void EntityMapper_EnumProperties_ShouldMapCorrectly()
        {
            var columns = new List<string> { "name", "side", "optional_side" };
            var ctor = EntityMapper.ResolveConstructor<EnumEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<EnumEntity>(ctor, columns);

            var rawValues1 = new string[] { "Order1", "Buy", "Sell" };
            var entity1 = factory(rawValues1);

            Assert.IsNotNull(entity1);
            Assert.AreEqual("Order1", entity1.Name);
            Assert.AreEqual(MarketSide.Buy, entity1.Side);
            Assert.AreEqual(MarketSide.Sell, entity1.OptionalSide);

            var rawValues2 = new string[] { "Order2", "sell", "" };
            var entity2 = factory(rawValues2);

            Assert.AreEqual("Order2", entity2.Name);
            Assert.AreEqual(MarketSide.Sell, entity2.Side);
            Assert.IsNull(entity2.OptionalSide);
        }

        [TestMethod]
        public void EntityMapper_InvalidDirtyData_ShouldThrowInvalidDataException_WithContext()
        {
            var columns = new List<string> { "name", "price", "weight", "count", "timestamp", "is_active", "date" };
            var ctor = EntityMapper.ResolveConstructor<MultiTypeEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<MultiTypeEntity>(ctor, columns);

            // 1. 浮点数字段传入损坏的文本
            var dirtyDouble = new string[] { "AAPL", "not_a_double", "0.75", "100", "1625097600000", "true", "2025-01-15" };
            var exDouble = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyDouble));
            StringAssert.Contains(exDouble.Message, "not_a_double");
            StringAssert.Contains(exDouble.Message, "Price");

            // 2. 整型字段传入损坏的文本
            var dirtyInt = new string[] { "AAPL", "150.25", "0.75", "abc", "1625097600000", "true", "2025-01-15" };
            var exInt = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyInt));
            StringAssert.Contains(exInt.Message, "abc");
            StringAssert.Contains(exInt.Message, "Count");

            // 3. 枚举字段传入未定义枚举值
            var enumColumns = new List<string> { "name", "side", "optional_side" };
            var enumCtor = EntityMapper.ResolveConstructor<EnumEntity>(enumColumns);
            var enumFactory = EntityMapper.GetOrCreateCompiledFactory<EnumEntity>(enumCtor, enumColumns);

            var dirtyEnum = new string[] { "OrderX", "UnknownSide", "" };
            var exEnum = Assert.ThrowsException<System.IO.InvalidDataException>(() => enumFactory(dirtyEnum));
            StringAssert.Contains(exEnum.Message, "UnknownSide");
            StringAssert.Contains(exEnum.Message, "Side");
        }

        [TestMethod]
        public void EntityMapper_DecimalAndNullableDecimal_ShouldMapCorrectly()
        {
            var columns = new List<string> { "symbol", "amount", "cash_bonus" };
            var ctor = EntityMapper.ResolveConstructor<DecimalEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<DecimalEntity>(ctor, columns);

            var rawValues1 = new string[] { "sh.600000", "1234567.89", "0.55" };
            var entity1 = factory(rawValues1);

            Assert.IsNotNull(entity1);
            Assert.AreEqual("sh.600000", entity1.Symbol);
            Assert.AreEqual(1234567.89m, entity1.Amount);
            Assert.AreEqual(0.55m, entity1.CashBonus);

            // 空值测试
            var rawValues2 = new string[] { "sh.600000", "", "" };
            var entity2 = factory(rawValues2);

            Assert.AreEqual(0m, entity2.Amount);
            Assert.IsNull(entity2.CashBonus);

            // 脏数据测试
            var dirtyValues = new string[] { "sh.600000", "not_a_decimal", "0.55" };
            var ex = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyValues));
            StringAssert.Contains(ex.Message, "not_a_decimal");
            StringAssert.Contains(ex.Message, "Amount");
        }

        [TestMethod]
        public void DataRow_GetDecimal_ShouldWorkCorrectly()
        {
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["amount"] = 0,
                ["empty"] = 1,
                ["invalid"] = 2
            };
            var values = new string[] { "9999.88", "", "bad_number" };
            var row = new DataRow(values, ordinals);

            Assert.AreEqual(9999.88m, row.GetDecimal("amount"));
            Assert.AreEqual(0m, row.GetDecimal("empty"));
            Assert.AreEqual(12.34m, row.GetDecimal("empty", 12.34m));
            Assert.AreEqual(50m, row.GetDecimal("invalid", 50m));
            Assert.AreEqual(0m, row.GetDecimal("non_existent"));
        }

        [TestMethod]
        public void EntityMapper_DirtyDataGuards_AllPrimitiveTypes_ThrowsInvalidDataException()
        {
            var columns = new List<string> { "name", "price", "weight", "count", "timestamp", "is_active", "date" };
            var ctor = EntityMapper.ResolveConstructor<MultiTypeEntity>(columns);
            var factory = EntityMapper.GetOrCreateCompiledFactory<MultiTypeEntity>(ctor, columns);

            // 1. float 脏数据
            var dirtyFloat = new[] { "AAPL", "150.0", "not_a_float", "10", "1000", "true", "2025-01-01" };
            var exFloat = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyFloat));
            StringAssert.Contains(exFloat.Message, "not_a_float");
            StringAssert.Contains(exFloat.Message, "Weight");

            // 2. long 脏数据
            var dirtyLong = new[] { "AAPL", "150.0", "1.5", "10", "not_a_long", "true", "2025-01-01" };
            var exLong = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyLong));
            StringAssert.Contains(exLong.Message, "not_a_long");
            StringAssert.Contains(exLong.Message, "Timestamp");

            // 3. bool 脏数据
            var dirtyBool = new[] { "AAPL", "150.0", "1.5", "10", "1000", "not_a_bool", "2025-01-01" };
            var exBool = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyBool));
            StringAssert.Contains(exBool.Message, "not_a_bool");
            StringAssert.Contains(exBool.Message, "IsActive");

            // 4. DateTime 脏数据
            var dirtyDate = new[] { "AAPL", "150.0", "1.5", "10", "1000", "true", "not_a_date" };
            var exDate = Assert.ThrowsException<System.IO.InvalidDataException>(() => factory(dirtyDate));
            StringAssert.Contains(exDate.Message, "not_a_date");
            StringAssert.Contains(exDate.Message, "Date");
        }
    }
}
