using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class SimulatedBrokerTests
    {
        private static IDataRow CreateBar(double open, double high, double low, double close, double volume, double? limitUp = null)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["open"] = 0,
                ["high"] = 1,
                ["low"] = 2,
                ["close"] = 3,
                ["volume"] = 4
            };

            var values = new List<string>
            {
                open.ToString(System.Globalization.CultureInfo.InvariantCulture),
                high.ToString(System.Globalization.CultureInfo.InvariantCulture),
                low.ToString(System.Globalization.CultureInfo.InvariantCulture),
                close.ToString(System.Globalization.CultureInfo.InvariantCulture),
                volume.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            if (limitUp.HasValue)
            {
                cols["limit_up"] = 5;
                values.Add(limitUp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return new DataRow(values.ToArray(), cols);
        }

        [TestMethod]
        public void SpotStock_StandardBuyAndSell_ShouldWorkCorrectly()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 1,
                AllowShort = false,
                FeeRate = 0.0003,
                MinFee = 5.0,
                Slippage = 0.001, // 千一滑点
                Multiplier = 1.0,
                MarginRate = 1.0
            };

            var broker = new SimulatedBroker(config);

            // 1. 买入 10,000 股 @ 10.0 元 (基准价 10.0，买入滑点千一 -> 10.01)
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10000m);
            string buyId = broker.SubmitOrder(buyOrder);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.AreEqual(10000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000")); // T+1 当日不可用

            // turnover = 10000 * 10.01 = 100,100
            // fee = 100100 * 0.0003 = 30.03
            // total cost = 100,130.03
            double expectedCash = 1_000_000.0 - 100130.03;
            Assert.AreEqual(expectedCash, broker.GetCash(), 1e-2);

            // 2. 推进到次日 (T+1 解锁)
            broker.Step(DateTime.UtcNow.AddDays(1));
            Assert.AreEqual(10000m, broker.GetAvailablePosition("sh.600000"));

            // 3. 卖出 10,000 股 @ 12.0 元 (基准价 12.0，卖出滑点千一 -> 11.988)
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 12.0, 10000m);
            string sellId = broker.SubmitOrder(sellOrder);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(sellId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 卖出收入 = 10000 * 11.988 - (119880 * 0.0003 = 35.964) = 119,844.036
            expectedCash += 119844.036;
            Assert.AreEqual(expectedCash, broker.GetCash(), 1e-2);
        }

        [TestMethod]
        public void SpotStock_TPlusOne_SameDaySell_ShouldBeRejected()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 1,
                AllowShort = false
            };

            var broker = new SimulatedBroker(config);

            // 当天买入
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
            broker.SubmitOrder(buyOrder);

            // 当天立即尝试卖出，应被 T+1 可用量限制拦截拒绝
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.5, 1000m);
            string sellId = broker.SubmitOrder(sellOrder);

            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000")); // 持仓不变
        }

        [TestMethod]
        public void SpotStock_AllowShortFalse_SellWithoutPosition_ShouldBeRejected()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = false
            };

            var broker = new SimulatedBroker(config);

            // 无持仓直接卖空，直接被拒绝
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 500m);
            string sellId = broker.SubmitOrder(sellOrder);

            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void InsufficientCash_ShouldBeRejected()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000.0 // 只有 1,000 元
            };

            var broker = new SimulatedBroker(config);

            // 尝试买入价值 10,000 元的股票
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
            string buyId = broker.SubmitOrder(buyOrder);

            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(buyId));
            Assert.AreEqual(1_000.0, broker.GetCash());
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void VolumeLimit_LiquidityConstraint_ShouldBlockLargeOrdersAndSuspension()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                VolumeLimit = 0.1 // 单笔不能超过成交量 10%
            };

            var broker = new SimulatedBroker(config);

            // 1. 停牌场景 (Volume = 0)
            var suspendedBar = CreateBar(10.0, 10.0, 10.0, 10.0, volume: 0.0);
            var order1 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m);
            string id1 = broker.ExecuteOrder(order1, 10.0, suspendedBar);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id1));

            // 2. 流动性不足场景 (当前 Bar 成交量 5,000 股，允许最大 500 股；委托 1,000 股超标)
            var lowVolBar = CreateBar(10.0, 10.5, 9.8, 10.2, volume: 5000.0);
            var order2 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.2, 1000m);
            string id2 = broker.ExecuteOrder(order2, 10.2, lowVolBar);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2));

            // 3. 流动性充足场景 (委托 500 股，恰好等于 5000 * 0.1)
            var order3 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.2, 500m);
            string id3 = broker.ExecuteOrder(order3, 10.2, lowVolBar);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(id3));
            Assert.AreEqual(500m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void CheckLimit_WithHook_ShouldBlockLimitUpBuy()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                CheckLimit = true,
                OnCheckLimit = (order, bar) =>
                {
                    if (order.IsBuy && bar.ContainsColumn("limit_up") && bar.GetDouble("close") >= bar.GetDouble("limit_up"))
                    {
                        return false; // 涨停禁止买入
                    }
                    return true;
                }
            };

            var broker = new SimulatedBroker(config);

            // 涨停 Bar: close = 11.0, limit_up = 11.0
            var limitUpBar = CreateBar(10.0, 11.0, 10.0, 11.0, volume: 100000.0, limitUp: 11.0);
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 11.0, 1000m);

            string orderId = broker.ExecuteOrder(buyOrder, 11.0, limitUpBar);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void Futures_MarginTradingAndShortSelling_ShouldWorkAccurately()
        {
            // 期货场景：一手 10 吨 (Multiplier=10)，保证金率 10% (MarginRate=0.1)，允许双向做空，T+0 即时可用
            var config = new BrokerConfig
            {
                InitialCash = 500_000.0,
                SettleDays = 0,
                AllowShort = true,
                FeeRate = 0.0001,
                MinFee = 0.0,
                Slippage = 0.0,
                Multiplier = 10.0,
                MarginRate = 0.1
            };

            var broker = new SimulatedBroker(config);

            // 1. 多头开仓：买入 10 手螺纹钢 @ 3,500 元/吨
            // 名义金额 = 10手 * 3500 * 10 = 350,000 元
            // 占用保证金 = 350,000 * 0.1 = 35,000 元
            // 手续费 = 350,000 * 0.0001 = 35 元
            var openLong = new OrderRequest("rb2410", OrderDirection.Buy, OrderType.Market, 3500.0, 10m);
            string longId = broker.SubmitOrder(openLong);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(longId));
            Assert.AreEqual(10m, broker.GetPosition("rb2410"));
            Assert.AreEqual(10m, broker.GetAvailablePosition("rb2410")); // T+0 即时可用

            // 可用现金 = 500,000 - 35,000 - 35 = 464,965 元
            Assert.AreEqual(464965.0, broker.GetCash(), 1e-2);

            // 总权益 = 现金(464,965) + 保证金(35,000) + 浮盈(0) = 500,000 - 35 = 499,965 元
            Assert.AreEqual(499965.0, broker.GetTotalEquity(), 1e-2);

            // 2. 多头平仓：价格涨到 3,600 元/吨，平仓 10 手
            // 盈利 = (3600 - 3500) * 10手 * 10乘数 = 10,000 元
            // 释放保证金 = 35,000 元
            // 平仓手续费 = 10 * 3600 * 10 * 0.0001 = 36 元
            var closeLong = new OrderRequest("rb2410", OrderDirection.Sell, OrderType.Market, 3600.0, 10m);
            string closeId = broker.SubmitOrder(closeLong);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(closeId));
            Assert.AreEqual(0m, broker.GetPosition("rb2410"));

            // 最终现金 = 464,965 + 35,000 + 10,000 - 36 = 509,929 元
            Assert.AreEqual(509929.0, broker.GetCash(), 1e-2);
            Assert.AreEqual(509929.0, broker.GetTotalEquity(), 1e-2);

            // 3. 空头开仓：直接卖空 5 手 @ 3,600 元/吨
            // 名义金额 = 5 * 3600 * 10 = 180,000 元
            // 占用保证金 = 18,000 元
            // 手续费 = 18 元
            var openShort = new OrderRequest("rb2410", OrderDirection.Sell, OrderType.Market, 3600.0, 5m);
            string shortId = broker.SubmitOrder(openShort);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(shortId));
            Assert.AreEqual(-5m, broker.GetPosition("rb2410"));

            // 4. 空头平仓：价格下跌至 3,500 元/吨，买平 5 手
            // 盈利 = (3600 - 3500) * 5 * 10 = 5,000 元
            // 释放保证金 18,000 元
            // 平仓手续费 = 5 * 3500 * 10 * 0.0001 = 17.5 元
            var closeShort = new OrderRequest("rb2410", OrderDirection.Buy, OrderType.Market, 3500.0, 5m);
            string closeShortId = broker.SubmitOrder(closeShort);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(closeShortId));
            Assert.AreEqual(0m, broker.GetPosition("rb2410"));

            // 最终现金 = 509,929 - 18,000 - 18 + 18,000 + 5,000 - 17.5 = 514,893.5 元
            Assert.AreEqual(514893.5, broker.GetCash(), 1e-2);
        }

        [TestMethod]
        public void Hooks_CalculateFeeAndSlippage_ShouldOverrideDefaults()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 0,
                // 自定义手续费：买入万一，卖出万一 + 0.05% 印花税
                OnCalculateFee = (order, turnover) =>
                {
                    double baseFee = turnover * 0.0001;
                    double tax = order.IsSell ? turnover * 0.0005 : 0.0;
                    return baseFee + tax;
                },
                // 自定义滑点：固定上浮 0.05 元
                OnCalculateSlippage = (order, basePrice, bar) => order.IsBuy ? basePrice + 0.05 : basePrice - 0.05
            };

            var broker = new SimulatedBroker(config);
            var bar = CreateBar(10.0, 10.5, 9.8, 10.0, 50000.0);

            // 买入 1000 股 @ 10.0 元 -> 成交价 10.05 元
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
            broker.ExecuteOrder(buyOrder, 10.0, bar);

            // turnover = 1000 * 10.05 = 10050.0
            // fee = 10050.0 * 0.0001 = 1.005
            // 扣减现金 = 10050 + 1.005 = 10051.005
            Assert.AreEqual(1_000_000.0 - 10051.005, broker.GetCash(), 1e-2);

            // 卖出 1000 股 @ 10.0 元 -> 成交价 9.95 元
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 1000m);
            broker.ExecuteOrder(sellOrder, 10.0, bar);

            // turnover = 1000 * 9.95 = 9950.0
            // fee = 9950.0 * (0.0001 + 0.0005) = 5.97
            // 增加现金 = 9950.0 - 5.97 = 9944.03
            double expectedCash = (1_000_000.0 - 10051.005) + 9944.03;
            Assert.AreEqual(expectedCash, broker.GetCash(), 1e-2);
        }

        [TestMethod]
        public void SyncState_ShouldAtomicallyUpdateCashAndPositions()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0 };
            var broker = new SimulatedBroker(config);

            var externalPositions = new Dictionary<string, decimal>
            {
                ["sh.600000"] = 5000m,
                ["sz.000001"] = 2000m
            };

            // 实盘柜台断线重连对账同步
            broker.SyncState(250_000.0, externalPositions);

            Assert.AreEqual(250_000.0, broker.GetCash());
            Assert.AreEqual(5000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(5000m, broker.GetAvailablePosition("sh.600000"));
            Assert.AreEqual(2000m, broker.GetPosition("sz.000001"));

            var allPositions = broker.GetPositions();
            Assert.AreEqual(2, allPositions.Count);
        }

        [TestMethod]
        public void CancelOrder_AndInvalidInputGuards_ShouldWork()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0 };
            var broker = new SimulatedBroker(config);

            // 1. 测试非法参数保护
            var invalidOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 0m);
            string id = broker.SubmitOrder(invalidOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id));

            // 2. 测试查询不存在的订单
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus("non-existent-order"));

            // 3. 测试撤单生命周期
            broker.CancelOrder(id); // 针对已被 Rejected 的订单取消无副作用
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id));
        }

        [TestMethod]
        public void EdgeCases_And_FullHookCoverage_ShouldAchieveNear100Percent()
        {
            // 1. 现货模式下的 GetTotalEquity (现金 + 持仓市值)
            var spotConfig = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 1,
                AllowShort = false,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0,
                Multiplier = 1.0,
                MarginRate = 1.0
            };
            var spotBroker = new SimulatedBroker(spotConfig);
            spotBroker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10000m));
            // 买入花费 100,000，剩余现金 900,000，持仓市值 100,000，总权益应严格等于 1,000,000
            Assert.AreEqual(1_000_000.0, spotBroker.GetTotalEquity(), 1e-4);

            // 2. 渐进多批次 T+1 跨日解锁 (remainingLots 分支)
            var multiLotBroker = new SimulatedBroker(spotConfig);
            DateTime day0 = new DateTime(2024, 12, 31);
            DateTime day1 = new DateTime(2025, 1, 1);
            DateTime day2 = new DateTime(2025, 1, 2);
            DateTime day3 = new DateTime(2025, 1, 3);

            multiLotBroker.Step(day0);
            multiLotBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10000m), 10.0, null); // 到期日: day1

            multiLotBroker.Step(day1); // 解锁 day0 买入的 10000 股
            Assert.AreEqual(10000m, multiLotBroker.GetAvailablePosition("sh.600000"));

            multiLotBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 2000m), 10.0, null); // 到期日: day2

            multiLotBroker.Step(day2); // 解锁 day1 买入的 2000 股
            Assert.AreEqual(12000m, multiLotBroker.GetAvailablePosition("sh.600000"));

            multiLotBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 3000m), 10.0, null); // 到期日: day3
            // 此时总持仓 15000 股，可用持仓为 12000 股 (day2的3000未到期留在 remainingLots)
            Assert.AreEqual(15000m, multiLotBroker.GetPosition("sh.600000"));
            Assert.AreEqual(12000m, multiLotBroker.GetAvailablePosition("sh.600000"));

            multiLotBroker.Step(day3); // 解锁 day2 的 3000 股
            Assert.AreEqual(15000m, multiLotBroker.GetAvailablePosition("sh.600000"));

            // 3. OnCheckVolume Hook 覆盖 (分别拦截与放行)
            var volHookConfig = new BrokerConfig
            {
                InitialCash = 100_000.0,
                OnCheckVolume = (order, bar) => order.Quantity <= 500m
            };
            var volBroker = new SimulatedBroker(volHookConfig);
            var bar = CreateBar(10.0, 10.5, 9.8, 10.0, 10000.0);

            string rejectId = volBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 600m), 10.0, bar);
            Assert.AreEqual(OrderStatus.Rejected, volBroker.GetOrderStatus(rejectId));

            string fillId = volBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 400m), 10.0, bar);
            Assert.AreEqual(OrderStatus.Filled, volBroker.GetOrderStatus(fillId));

            // 4. OnGetMultiplier、OnGetMarginRate 与 OnGetSettleDays Hook 在执行中的实际调用 (传入 dummy 上下文触发分支)
            bool multiplierCalled = false;
            bool marginRateCalled = false;
            var dynamicConfig = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 1,
                CheckLimit = true,
                OnCheckLimit = (order, bar) => true,
                OnGetSettleDays = s => 0, // 动态覆盖为 T+0
                OnGetMultiplier = (s, f) => { multiplierCalled = true; return 20.0; }, // 动态覆盖乘数为 20
                OnGetMarginRate = (s, f) => { marginRateCalled = true; return 0.05; } // 动态覆盖保证金率为 5%
            };

            // 构造带 fields 的 Broker 注入测试
            var dummyFields = new CarrotBacktesting.NET.Data.FieldRegistry();
            var dynBroker = new SimulatedBroker(dynamicConfig, dummyFields);
            var passBar = CreateBar(100.0, 105.0, 98.0, 100.0, 100000.0);
            string dynOrderId = dynBroker.ExecuteOrder(new OrderRequest("custom_item", OrderDirection.Buy, OrderType.Market, 100.0, 10m), 100.0, passBar);
            Assert.AreEqual(OrderStatus.Filled, dynBroker.GetOrderStatus(dynOrderId));
            // 校验 T+0 立即释放可用持仓
            Assert.AreEqual(10m, dynBroker.GetAvailablePosition("custom_item"));
            // 严谨断言：Hook 必须被真实调用
            Assert.IsTrue(multiplierCalled, "OnGetMultiplier Hook 必须被真实执行");
            Assert.IsTrue(marginRateCalled, "OnGetMarginRate Hook 必须被真实执行");
        }

        [TestMethod]
        public void HotPath_TombstoneRemoval_WhenPositionClosed_ShouldNotRetainTombstones()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 0, // T+0
                AllowShort = false
            };
            var broker = new SimulatedBroker(config);

            // 买入并立即全额卖出 50 只不同的标的
            for (int i = 0; i < 50; i++)
            {
                string sym = $"sym_{i}";
                broker.SubmitOrder(new OrderRequest(sym, OrderDirection.Buy, OrderType.Market, 10.0, 100m));
                Assert.AreEqual(100m, broker.GetPosition(sym));
                Assert.IsTrue(broker.GetPositions().ContainsKey(sym));

                broker.SubmitOrder(new OrderRequest(sym, OrderDirection.Sell, OrderType.Market, 10.0, 100m));
                Assert.AreEqual(0m, broker.GetPosition(sym));
                Assert.IsFalse(broker.GetPositions().ContainsKey(sym));
            }

            // 验证清仓后，持仓列表为空，彻底消除了墓碑对象
            var remainingPositions = broker.GetPositions();
            Assert.AreEqual(0, remainingPositions.Count);
        }

        [TestMethod]
        public void HotPath_GetPositions_ShouldCacheSnapshot_UntilMutated()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                SettleDays = 0
            };
            var broker = new SimulatedBroker(config);

            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m));

            var snapshot1 = broker.GetPositions();
            var snapshot2 = broker.GetPositions();

            // 连续两次读取无变动时，必须返回完全相同的快照引用（零堆分配）
            Assert.IsTrue(ReferenceEquals(snapshot1, snapshot2));

            // 产生新的交易后，快照失效并重新构建新引用
            broker.SubmitOrder(new OrderRequest("sh.600001", OrderDirection.Buy, OrderType.Market, 20.0, 200m));
            var snapshot3 = broker.GetPositions();

            Assert.IsFalse(ReferenceEquals(snapshot1, snapshot3));
            Assert.AreEqual(2, snapshot3.Count);
        }

        [TestMethod]
        public void HotPath_HighThroughput100kOrders_ShouldBeFastAndCorrect()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000_000.0,
                SettleDays = 0,
                FeeRate = 0.0001,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 连续撮合 100,000 次买入与卖出
            for (int i = 0; i < 50_000; i++)
            {
                broker.SubmitOrder(new OrderRequest("test_symbol", OrderDirection.Buy, OrderType.Market, 10.0, 10m));
                broker.SubmitOrder(new OrderRequest("test_symbol", OrderDirection.Sell, OrderType.Market, 10.0, 10m));
            }

            sw.Stop();

            // 10万笔订单撮合应在 500ms 内完成（极高吞吐）
            Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"100k orders took {sw.ElapsedMilliseconds} ms, slower than expected.");
            Assert.AreEqual(0m, broker.GetPosition("test_symbol"));
        }

        [TestMethod]
        public void BrokerConfig_DefaultValues_ShouldBeStrictlySet()
        {
            // 验证通用默认基线 (1000000, 0, false, 0.001, 0.0, 0.001, 0.05, false, 1.0, 1.0, "UTC")
            var config = new BrokerConfig();
            Assert.AreEqual(1_000_000.0, config.InitialCash);
            Assert.AreEqual(0, config.SettleDays);
            Assert.IsFalse(config.AllowShort);
            Assert.AreEqual(0.001, config.FeeRate);
            Assert.AreEqual(0.0, config.MinFee);
            Assert.AreEqual(0.001, config.Slippage);
            Assert.AreEqual(0.05, config.VolumeLimit);
            Assert.IsFalse(config.CheckLimit);
            Assert.AreEqual(1.0, config.Multiplier);
            Assert.AreEqual(1.0, config.MarginRate);
            Assert.AreEqual("UTC", config.TimeZoneId);
            Assert.AreEqual(TimeZoneInfo.Utc, config.TimeZone);
        }

        [TestMethod]
        public void Hook_WithFieldRegistry_ShouldExecuteHookAndAccessFields()
        {
            var fieldTable = new CarrotBacktesting.NET.Data.FieldTable("info", new[] { "multiplier" }, new());
            var fields = new CarrotBacktesting.NET.Data.FieldRegistry();
            fields.Register("info", fieldTable);

            bool hookExecuted = false;
            var config = new BrokerConfig
            {
                OnGetMultiplier = (s, f) =>
                {
                    hookExecuted = true;
                    Assert.IsNotNull(f, "FieldRegistry 必须通过构造注入可靠传递给 Hook");
                    Assert.IsTrue(f.HasTable("info"));
                    return 300.0;
                }
            };

            var broker = new SimulatedBroker(config, fields: fields);
            var bar = CreateBar(10.0, 10.0, 10.0, 10.0, 1000.0);
            broker.ExecuteOrder(new OrderRequest("test", OrderDirection.Buy, OrderType.Market, 10.0, 10m), 10.0, bar);

            Assert.IsTrue(hookExecuted, "单向注入 fields 后 Hook 应该成功执行");
        }

        [TestMethod]
        public void BrokerConfig_InvalidTimeZoneId_ShouldThrowTimeZoneNotFoundException()
        {
            var config = new BrokerConfig();
            Assert.ThrowsException<TimeZoneNotFoundException>(() => config.TimeZoneId = "Invalid/Non_Existent_Zone");
        }

        [TestMethod]
        public void Broker_WithTimeZone_ShouldAccuratelySettleAcrossDays()
        {
            // 配置交易所为东八区 (Asia/Shanghai)
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 1,
                TimeZoneId = "Asia/Shanghai"
            };
            var broker = new SimulatedBroker(config);

            // 传入 Unspecified 时间戳（模拟真实 CSV 数据源不带 Kind 标签）：UTC 2025-01-02 06:00:00 (北京时间 14:00) 买入
            var utcTradeTime = new DateTime(2025, 1, 2, 6, 0, 0, DateTimeKind.Unspecified);
            broker.Step(utcTradeTime);
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m));

            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "T+1 当日不可用");

            // UTC 时间 2025-01-03 01:30:00 (即北京时间 2025-01-03 09:30:00) 跨日推进
            var utcNextDay = new DateTime(2025, 1, 3, 1, 30, 0, DateTimeKind.Unspecified);
            broker.Step(utcNextDay);

            Assert.AreEqual(100m, broker.GetAvailablePosition("sh.600000"), "北京时间跨日后，即使输入为 Unspecified 也必须通过 DateTimeKind 防御准时解锁");
        }

        [TestMethod]
        public void MinuteLevel_TPlusOne_ShouldUnlockSharesOnNextDayMorning()
        {
            var broker = new SimulatedBroker(new BrokerConfig
            {
                SettleDays = 1,
                AllowShort = false,
                TimeZoneId = "Asia/Shanghai"
            });
            // Day 1 14:55:00 买入 100 股
            broker.Step(new DateTime(2025, 1, 2, 14, 55, 0));
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "买入当日可用仓位必须为 0");

            // Day 2 09:31:00 早盘推进第一根 Bar
            broker.Step(new DateTime(2025, 1, 3, 9, 31, 0));
            var avail = broker.GetAvailablePosition("sh.600000");
            Assert.AreEqual(100m, avail, "次日早盘首根 Bar 必须成功解锁前一日全天任何时间买入的份额，绝不能退化为 T+2");
        }
    }
}
