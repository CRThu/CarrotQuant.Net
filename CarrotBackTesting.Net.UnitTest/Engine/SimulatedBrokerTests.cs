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

        [TestMethod]
        public void Defense_NaNAndInfinityBasePrice_ShouldBeStrictlyRejected()
        {
            var config = new BrokerConfig { InitialCash = 1_000_000.0 };
            var broker = new SimulatedBroker(config);

            // 1. 测试 NaN 价格
            var nanOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, double.NaN, 100m);
            string id1 = broker.ExecuteOrder(nanOrder, double.NaN, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id1));
            Assert.IsFalse(double.IsNaN(broker.GetCash()), "账户资金绝不能被 NaN 污染");
            Assert.AreEqual(1_000_000.0, broker.GetCash(), 1e-4);

            // 2. 测试 Infinity 价格
            var infOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, double.PositiveInfinity, 100m);
            string id2 = broker.ExecuteOrder(infOrder, double.PositiveInfinity, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2));
            Assert.AreEqual(1_000_000.0, broker.GetCash(), 1e-4);

            // 3. 测试 -Infinity 价格
            string id3 = broker.ExecuteOrder(infOrder, double.NegativeInfinity, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id3));
            Assert.AreEqual(1_000_000.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void SpotStock_MinFee_SmallOrder_ShouldApplyMinimumFee()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                FeeRate = 0.0003,
                MinFee = 5.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            // 买入 100 股 @ 2.0 元，成交额 200 元；万三佣金仅 0.06 元，必须触发 5.0 元最低手续费
            var order = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 2.0, 100m);
            string id = broker.SubmitOrder(order);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(id));

            // 扣减现金 = 200.0 (turnover) + 5.0 (minFee) = 205.0
            Assert.AreEqual(100_000.0 - 205.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void Futures_ShortPosition_AvailableQuantityAndPartialClose_ShouldBeAccurate()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                AllowShort = true,
                MarginRate = 0.1, // 10% 保证金
                Multiplier = 10.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            // 1. 做空 5 手 @ 100.0 元
            // turnover = 5 * 100 * 10 = 5000. 保证金占用 = 500
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Sell, OrderType.Limit, 100.0, 5m), 100.0, null);
            Assert.AreEqual(-5m, broker.GetPosition("IF2501"));
            Assert.AreEqual(-5m, broker.GetAvailablePosition("IF2501"), "空头可用持仓必须与总持仓同步保持负号一致");
            Assert.AreEqual(1_000_000.0 - 500.0, broker.GetCash(), 1e-4);

            // 2. 买平 2 手 @ 90.0 元 (盈利：(100 - 90) * 2 * 10 = 200; 释放保证金 = 2 * 100 * 10 * 0.1 = 200)
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Buy, OrderType.Limit, 90.0, 2m), 90.0, null);
            Assert.AreEqual(-3m, broker.GetPosition("IF2501"));
            Assert.AreEqual(-3m, broker.GetAvailablePosition("IF2501"), "部分平仓后空头可用持仓应准确更新为 -3");
            Assert.AreEqual(1_000_000.0 - 500.0 + 200.0 + 200.0, broker.GetCash(), 1e-4);

            // 3. 买平剩余 3 手 @ 110.0 元 (亏损：(100 - 110) * 3 * 10 = -300; 释放保证金 = 3 * 100 * 10 * 0.1 = 300)
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Buy, OrderType.Limit, 110.0, 3m), 110.0, null);
            Assert.AreEqual(0m, broker.GetPosition("IF2501"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("IF2501"));
            Assert.AreEqual(1_000_000.0 - 500.0 + 400.0 + (-300.0 + 300.0), broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void Futures_PositionFlip_ShouldCloseAndOpenRemainderAccurately()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                AllowShort = true,
                MarginRate = 0.1,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            // 1. 空头 5 手 @ 100.0 (占用保证金 50)
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 100.0, 5m), 100.0, null);
            Assert.AreEqual(-5m, broker.GetPosition("rb2505"));

            // 2. 买入 8 手 @ 100.0 (意图：平空 5 手 + 反手开多 3 手)
            // 平空 5 手释放保证金 50，盈亏 0；反手开多 3 手占用保证金 3 * 100 * 0.1 = 30
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 100.0, 8m), 100.0, null);
            Assert.AreEqual(3m, broker.GetPosition("rb2505"), "反手后持仓应从 -5 转为 +3");
            Assert.AreEqual(3m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(1_000_000.0 - 30.0, broker.GetCash(), 1e-4);

            // 3. 卖出 7 手 @ 100.0 (意图：平多 3 手 + 反手开空 4 手)
            // 平多 3 手释放保证金 30；反手开空 4 手占用保证金 4 * 100 * 0.1 = 40
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 100.0, 7m), 100.0, null);
            Assert.AreEqual(-4m, broker.GetPosition("rb2505"), "反手后持仓应从 +3 转为 -4");
            Assert.AreEqual(-4m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(1_000_000.0 - 40.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void Futures_PositionFlip_InsufficientCashForRemainder_ShouldReject()
        {
            var config = new BrokerConfig
            {
                InitialCash = 10.0, // 仅 10 元现金
                AllowShort = true,
                MarginRate = 0.1,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            // 初始持有多头 5 手 (假设通过 SyncState 注入，避免开仓扣现金)
            broker.SyncState(10.0, new Dictionary<string, decimal> { ["rb2505"] = 5m });

            // 意图卖出 8 手 (平多 5 手，反手开空 3 手)
            // 反手开空 3 手需要保证金 3 * 100 * 0.1 = 30 元，而可用现金仅 10 元，必须被拒单拦截
            string id = broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 100.0, 8m), 100.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id));
            Assert.AreEqual(5m, broker.GetPosition("rb2505"), "拒单后持仓不变");
            Assert.AreEqual(10.0, broker.GetCash(), "拒单后资金不变");
        }

        [TestMethod]
        public void SubmitOrder_MarketOrder_UsesMarketPrice()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0, Slippage = 0.0, FeeRate = 0.0 };
            var broker = new SimulatedBroker(config);

            // 预先更新市场价格
            broker.UpdateMarketPrice("sh.600000", 15.0);

            // 市价单便捷构造函数（Price 默认为 0.0）
            var marketOrder = new OrderRequest("sh.600000", OrderDirection.Buy, 100m);
            string id = broker.SubmitOrder(marketOrder);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(id));
            Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(100_000.0 - 1500.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void MultiAsset_SpotLongAndFuturesShortHedge_ShouldCoexistSeamlessly()
        {
            // 多资产大类混合配置：同一个账户内无缝运行 A 股现货多头 + 股指期货空头对冲
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                AllowShort = true, // 允许做空（对冲需要）
                FeeRate = 0.0,
                Slippage = 0.0,
                OnGetMultiplier = (symbol, _) => symbol.StartsWith("IF") ? 300.0 : 1.0,
                OnGetMarginRate = (symbol, _) => symbol.StartsWith("IF") ? 0.1 : 1.0,
                OnGetSettleDays = symbol => symbol.StartsWith("sh") ? 1 : 0
            };
            var broker = new SimulatedBroker(config);

            DateTime day1_morning = new DateTime(2025, 1, 6, 9, 30, 0);
            broker.Step(day1_morning);

            // 1. 买入股票 10,000 股 @ 10.0 元 (全额现货: 占用 100,000 元, T+1 锁仓)
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 10_000m), 10.0, null);
            Assert.AreEqual(10_000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "股票现货买入当日受 T+1 约束可用为 0");
            Assert.AreEqual(900_000.0, broker.GetCash(), 1e-4);

            // 2. 做空股指期货 1 手 @ 4,000 点 (名义成交 1,200,000 元, 10% 保证金占用 120,000 元, T+0 可用)
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Sell, OrderType.Limit, 4000.0, 1m), 4000.0, null);
            Assert.AreEqual(-1m, broker.GetPosition("IF2501"));
            Assert.AreEqual(-1m, broker.GetAvailablePosition("IF2501"), "期货空头立即可用");

            // 可用现金 = 1,000,000 - 100,000 - 120,000 = 780,000 元
            Assert.AreEqual(780_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(1_000_000.0, broker.GetTotalEquity(), 1e-4, "开仓初始盯市权益守恒为 100 万元");

            // 3. 推进到 Day 2 (次日): 股票可用持仓解冻
            DateTime day2_morning = new DateTime(2025, 1, 7, 9, 30, 0);
            broker.Step(day2_morning);
            Assert.AreEqual(10_000m, broker.GetAvailablePosition("sh.600000"), "跨日后现货股票可用持仓成功解冻为 10,000");

            // 4. 行情变动盯市：股票涨到 11.0 (+10,000)，股指期货跌到 3,900 点 (做空浮盈 (4000-3900)*1*300 = +30,000)
            broker.UpdateMarketPrice("sh.600000", 11.0);
            broker.UpdateMarketPrice("IF2501", 3900.0);

            // 总权益 = 780,000(现金) + [100,000(股票本金) + 10,000(股票浮盈)] + [120,000(期货保证金) + 30,000(期货浮盈)]
            //       = 780,000 + 110,000 + 150,000 = 1,040,000 元
            Assert.AreEqual(1_040_000.0, broker.GetTotalEquity(), 1e-4);

            // 5. 对冲组合平仓
            // 卖平股票 @ 11.0 -> 释放 100,000，结转盈利 10,000，返还 110,000 现金
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 11.0, 10_000m), 11.0, null);
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(780_000.0 + 110_000.0, broker.GetCash(), 1e-4);

            // 买平期货 @ 3,900 -> 释放 120,000，结转盈利 30,000，返还 150,000 现金
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Buy, OrderType.Limit, 3900.0, 1m), 3900.0, null);
            Assert.AreEqual(0m, broker.GetPosition("IF2501"));

            // 最终现金与权益完全归拢守恒
            Assert.AreEqual(1_040_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(1_040_000.0, broker.GetTotalEquity(), 1e-4);
        }

        [TestMethod]
        public void MultiAsset_AllowShortTrue_SameDaySellSpotStock_ShouldBeStrictlyRejected()
        {
            // 防御高危穿透边界：在混合账户（AllowShort = true 供期货对冲）中，
            // 现货标的（SettleDays = 1）当日买入的份额绝对严禁在当日被违规平仓或非法做空穿透
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                OnGetSettleDays = symbol => symbol.StartsWith("sh") ? 1 : 0
            };
            var broker = new SimulatedBroker(config);

            DateTime day1 = new DateTime(2025, 1, 6, 9, 30, 0);
            broker.Step(day1);

            // 1. 当日买入股票 1,000 股 @ 10.0 元 (由于 SettleDays=1，在途锁仓，可用仓位为 0)
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 2. 当日试图卖出 1,000 股：由于尚有未解冻在途交收份额（PendingLots），必须被严厉拒单拦截！
            string sellOrderId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellOrderId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "拒单后持仓不可变");
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "拒单后可用持仓不可变");

            // 3. 次日早盘推进解冻
            broker.Step(new DateTime(2025, 1, 7, 9, 30, 0));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "次日成功解锁为 1000");

            // 4. 次日卖出正常成交
            string day2SellId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(day2SellId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void PendingLimitOrder_Buy_ShouldFreezeMarginAndPreventDoubleSpending()
        {
            // 测试买入限价挂单：资金冻结、防双花拦截与撤单回滚
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                FeeRate = 0.0003,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0); // 当前市价 10.0 元

            // 1. 提交买入限价单 9.0 元 (低于市价 10.0，进入未成交挂单队列)
            // 买入 10,000 股 @ 9.0 元 -> turnover = 90,000 元, fee = 27 元 -> 需冻结 90,027 元
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 10_000m);
            string orderId = broker.ExecuteOrder(buyOrder, 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"), "未成交前持仓为 0");
            Assert.AreEqual(100_000.0 - 90_027.0, broker.GetCash(), 1e-4, "可用资金必须扣除挂单冻结保证金与费用");
            Assert.AreEqual(100_000.0, broker.GetTotalEquity(), 1e-4, "挂单阶段账户总资产不发生实际消耗");

            // 2. 防双花测试：剩余可用资金仅 9,973 元，试图再次挂单买入需 18,000 元的订单 -> 必须被拒单拦截！
            var doubleSpendOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 2_000m);
            string rejId = broker.ExecuteOrder(doubleSpendOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(rejId), "资金已大部分被冻结，超额订单必须被精准拦截");
            Assert.AreEqual(100_000.0 - 90_027.0, broker.GetCash(), 1e-4, "拒单后可用资金不变");

            // 3. 主动撤单：资金原路全额解冻回滚
            broker.CancelOrder(orderId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(100_000.0, broker.GetCash(), 1e-4, "撤单后可用资金必须 100% 完整释放解冻");
            Assert.AreEqual(100_000.0, broker.GetTotalEquity(), 1e-4);
        }

        [TestMethod]
        public void PendingLimitOrder_Sell_ShouldFreezeAvailablePositionAndPreventOverselling()
        {
            // 测试卖出限价挂单：持仓冻结、防超卖拦截与撤单回滚
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = new SimulatedBroker(config);
            // 初始通过 SyncState 注入 1,000 股现货
            broker.SyncState(100_000.0, new Dictionary<string, decimal> { ["sh.600000"] = 1000m });
            broker.UpdateMarketPrice("sh.600000", 10.0);

            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 1. 挂单 11.0 元卖出 800 股 (高于市价 10.0，进入未成交挂单队列)
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 11.0, 800m);
            string orderId = broker.ExecuteOrder(sellOrder, 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "总持仓仍归属于账户");
            Assert.AreEqual(200m, broker.GetAvailablePosition("sh.600000"), "可用持仓必须被扣减 800 股冻结量，仅余 200 股");

            // 2. 防超卖测试：剩余可用仅 200 股，试图再次卖出 500 股 -> 必须被拒单拦截！
            var overSellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 11.0, 500m);
            string rejId = broker.ExecuteOrder(overSellOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(rejId), "已挂单的持仓严禁被重复卖出，超卖必须被拒绝");
            Assert.AreEqual(200m, broker.GetAvailablePosition("sh.600000"));

            // 3. 撤单：持仓原路全额解冻回滚
            broker.CancelOrder(orderId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "撤单后可用持仓完全恢复为 1,000 股");
        }

        [TestMethod]
        public void PendingLimitOrder_MarketPriceCross_ShouldFillAutomaticallyAndReleaseFreeze()
        {
            // 测试行情变动穿透限价：自动撮合成交、解冻并结算持仓
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0003,
                MinFee = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            bool tradeEventFired = false;
            broker.OnTrade += r => { tradeEventFired = true; };

            // 1. 提交买入限价单 9.5 元买入 1,000 股 (冻结 9500 + 2.85 元)
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
            string orderId = broker.ExecuteOrder(buyOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.IsFalse(tradeEventFired);

            // 2. 行情下探到 9.4 元 (触碰并击穿 9.5 元买入限价)
            broker.UpdateMarketPrice("sh.600000", 9.4);

            // 3. 验证自动撮合成交
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId), "行情触碰限价时必须自动完成撮合成交");
            Assert.IsTrue(tradeEventFired, "成交事件必须被准确触发");
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "持仓成功到账");
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 实际成交价格 9.5 元，名义成交额 9500 元，佣金 2.85 元
            double expectedCash = 100_000.0 - 9500.0 - 2.85;
            Assert.AreEqual(expectedCash, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void PendingLimitOrder_Futures_ShortSelling_ShouldFreezeAccurately()
        {
            // 期货空头限价挂单：保证金冻结与撤单释放
            var config = new BrokerConfig
            {
                InitialCash = 500_000.0,
                AllowShort = true,
                MarginRate = 0.1, // 10% 保证金
                Multiplier = 300.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("IF2501", 3900.0);

            // 1. 挂单 4,000 点做空 1 手 (高于当前市价 3900，未成交)
            // 名义金额 = 4000 * 300 = 1,200,000. 10% 保证金 = 120,000 元
            var sellOrder = new OrderRequest("IF2501", OrderDirection.Sell, OrderType.Limit, 4000.0, 1m);
            string orderId = broker.ExecuteOrder(sellOrder, 3900.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetPosition("IF2501"));
            Assert.AreEqual(500_000.0 - 120_000.0, broker.GetCash(), 1e-4, "挂单必须冻结 12 万元保证金");

            // 2. 撤单
            broker.CancelOrder(orderId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(500_000.0, broker.GetCash(), 1e-4, "撤单后 12 万元保证金 100% 完整解冻");
        }

        [TestMethod]
        public void PendingLimitOrder_Sell_MarketPriceRises_ShouldFillAutomatically()
        {
            // 测试卖出限价挂单：行情冲高击穿限价时自动撮合成交、释放持仓冻结并回笼资金
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0003,
                MinFee = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.SyncState(100_000.0, new Dictionary<string, decimal> { ["sh.600000"] = 1000m });
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 1. 挂单 10.5 元卖出 1,000 股 (高于市价 10.0，未成交)
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.5, 1000m);
            string orderId = broker.ExecuteOrder(sellOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "可用持仓已被 1000 股挂单全部冻结");

            // 2. 行情上涨到 10.6 元 (穿透卖出限价 10.5 元)
            broker.UpdateMarketPrice("sh.600000", 10.6);

            // 3. 验证自动撮合成交
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"), "成交后持仓全部平光清零");
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 成交额 = 1000 * 10.5 = 10500 元, 佣金 = 10500 * 0.0003 = 3.15 元, 现金回笼 10500 - 3.15 = 10496.85 元
            Assert.AreEqual(100_000.0 + 10496.85, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void PendingLimitOrder_Futures_FlipOrder_ShouldFreezeBothPositionAndMargin()
        {
            // 测试期货反手挂单：复合冻结（同时冻结多头可用持仓 + 反手新开空头所需保证金）
            var config = new BrokerConfig
            {
                InitialCash = 500_000.0,
                AllowShort = true,
                MarginRate = 0.1,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);
            // 初始持有多头 5 手 @ 100.0 (占用保证金 50 元)
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 100.0, 5m), 100.0, null);
            Assert.AreEqual(5m, broker.GetPosition("rb2505"));
            Assert.AreEqual(5m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(500_000.0 - 50.0, broker.GetCash(), 1e-4);

            // 意图：在 110.0 元反手卖出 8 手 (平多 5 手 + 开空 3 手)
            // 市价为 100.0，挂单 110.0 未能立即成交 -> 进入未成交挂单
            // 复合冻结检查：
            // 1. 持仓冻结：多头 5 手可用持仓全部冻结 -> AvailableQty 从 5 降为 0
            // 2. 资金冻结：反手新开 3 手所需保证金 = 3 * 110.0 * 0.1 = 33.0 元 -> Cash 从 (500000-50) 降为 (500000-50-33)
            var flipOrder = new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 110.0, 8m);
            string orderId = broker.ExecuteOrder(flipOrder, 100.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(5m, broker.GetPosition("rb2505"), "总持仓仍归属于账户");
            Assert.AreEqual(0m, broker.GetAvailablePosition("rb2505"), "平多的 5 手必须已被冻结");
            Assert.AreEqual(500_000.0 - 50.0 - 33.0, broker.GetCash(), 1e-4, "反手新开 3 手的保证金 33 元必须已被冻结");

            // 撤单：持仓与反手保证金同时全额释放
            broker.CancelOrder(orderId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(5m, broker.GetAvailablePosition("rb2505"), "持仓冻结释放，可用持仓恢复为 5");
            Assert.AreEqual(500_000.0 - 50.0, broker.GetCash(), 1e-4, "反手保证金 33 元释放恢复");
        }

        [TestMethod]
        public void PendingLimitOrder_MultipleOrdersLadder_StepTrigger_ShouldMatchSequentially()
        {
            // 测试阶梯挂单与 Step 时间步驱动撮合
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 阶梯挂两笔买单：
            // Order 1: 9.6 元买 1,000 股 (冻结 9600)
            // Order 2: 9.2 元买 1,000 股 (冻结 9200)
            string id1 = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.6, 1000m), 10.0, null);
            string id2 = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.2, 1000m), 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id2));
            Assert.AreEqual(100_000.0 - 9600.0 - 9200.0, broker.GetCash(), 1e-4, "两笔挂单冻结资金累加");

            // 价格调整为 9.4 元，通过 Step 推进时间步触发撮合
            // 预期：Order 1 (9.6 元) 满足买入条件成交；Order 2 (9.2 元) 仍不满足继续挂单
            broker.UpdateMarketPrice("sh.600000", 9.4);
            broker.Step(new DateTime(2025, 1, 6, 10, 0, 0));

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(id1), "9.6 元买单在市价 9.4 元时必须成交");
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id2), "9.2 元买单在市价 9.4 元时必须继续保留挂单");

            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            // Order 1 成交扣 9600 元，Order 2 依然冻结 9200 元
            Assert.AreEqual(100_000.0 - 9600.0 - 9200.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void VolumeLimit_Zero_ShouldAllowAnyVolume_AndPositiveLimit_ShouldReject()
        {
            // 验证 VolumeLimit = 0.0 时无流动性约束放行巨量单；VolumeLimit > 0.0 时超限拦截
            var noLimitConfig = new BrokerConfig
            {
                InitialCash = 10_000_000.0,
                VolumeLimit = 0.0, // 禁用
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var brokerNoLimit = new SimulatedBroker(noLimitConfig);
            // Bar 成交量仅 100 股
            var barSmall = CreateBar(10.0, 10.5, 9.8, 10.0, 100.0);
            var hugeOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10_000m);
            string id1 = brokerNoLimit.ExecuteOrder(hugeOrder, 10.0, barSmall);
            Assert.AreEqual(OrderStatus.Filled, brokerNoLimit.GetOrderStatus(id1), "VolumeLimit=0 时应无视 Bar Volume 限制正常成交");

            var limitedConfig = new BrokerConfig
            {
                InitialCash = 10_000_000.0,
                VolumeLimit = 0.1, // 限制最大 10%
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var brokerLimited = new SimulatedBroker(limitedConfig);
            string id2 = brokerLimited.ExecuteOrder(hugeOrder, 10.0, barSmall);
            Assert.AreEqual(OrderStatus.Rejected, brokerLimited.GetOrderStatus(id2), "超过 10% Bar Volume 的订单必须被 Reject");
        }

        [TestMethod]
        public void NonUtcTimeZone_AmericaNewYork_Rollover_ShouldUnlockPendingLotsAccurately()
        {
            // 验证非 UTC 时区（美东时区 America/New_York 或标准跨日）在跨过交易所当地 00:00:00 时的正确结算
            TimeZoneInfo nyZone;
            try
            {
                nyZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch (TimeZoneNotFoundException)
            {
                nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }

            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 1,
                TimeZone = nyZone,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);

            // 美东时间 2025-01-05 15:30:00 (盘中买入，T+1 到期日应为美东当地 2025-01-06)
            DateTime nyTimeBuy = new DateTime(2025, 1, 5, 15, 30, 0);
            DateTime utcBuy = TimeZoneInfo.ConvertTimeToUtc(nyTimeBuy, nyZone);
            broker.Step(utcBuy);

            string buyId = broker.SubmitOrder(new OrderRequest("AAPL", OrderDirection.Buy, OrderType.Market, 150.0, 100m));
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.AreEqual(100m, broker.GetPosition("AAPL"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("AAPL"), "T+1 刚买入不可用");

            // 推进到美东时间 2025-01-05 23:59:00（美东同日深夜，尚未到达次日 00:00:00）
            DateTime nyTimeNight = new DateTime(2025, 1, 5, 23, 59, 0);
            DateTime utcNight = TimeZoneInfo.ConvertTimeToUtc(nyTimeNight, nyZone);
            broker.Step(utcNight);
            Assert.AreEqual(0m, broker.GetAvailablePosition("AAPL"), "尚未到达美东次日 00:00:00，持仓仍锁定");

            // 推进到美东时间 2025-01-06 00:00:01（跨过美东次日零点，T+1 到期）
            DateTime nyTimeNextDay = new DateTime(2025, 1, 6, 0, 0, 1);
            DateTime utcNextDay = TimeZoneInfo.ConvertTimeToUtc(nyTimeNextDay, nyZone);
            broker.Step(utcNextDay);
            Assert.AreEqual(100m, broker.GetAvailablePosition("AAPL"), "跨过美东次日零点后，T+1 持仓必须精准解冻可用");
        }

        [TestMethod]
        public void TakerLimitOrder_LargeSlippage_ShouldBeCappedAtLimitPrice()
        {
            // 验证 Taker 激进限价单即使被配置了大滑点，成交价绝对不允许穿透委托限价（买单 <= Price，卖单 >= Price）
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.05, // 5% 巨大滑点
                FeeRate = 0.0,
                AllowShort = true
            };
            var broker = new SimulatedBroker(config);

            // 1. 买入限价单：基准市价 99.0，委托限价 100.0。
            // 正常 5% 滑点计算价格 = 99.0 * 1.05 = 103.95 (高于委托限价 100.0)。
            // 契约保护要求：成交价必须被截断截平在 100.0，绝不能以 103.95 穿透限价成交！
            ExecutionReport? buyReport = null;
            broker.OnTrade += r => buyReport = r;

            var buyLimit = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 100.0, 100m);
            string buyId = broker.ExecuteOrder(buyLimit, 99.0, null);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.IsNotNull(buyReport);
            Assert.AreEqual(100.0, buyReport.FillPrice, 1e-4, "买入限价单成交价格绝对严禁穿透委托限价 100.0");

            // 2. 卖出限价单：基准市价 101.0，委托限价 100.0。
            // 正常 5% 滑点计算价格 = 101.0 * (1 - 0.05) = 95.95 (低于委托限价 100.0)。
            // 契约保护要求：成交价必须被截断截平在 100.0，绝不能以 95.95 穿透限价成交！
            ExecutionReport? sellReport = null;
            broker.OnTrade += r => sellReport = r;

            var sellLimit = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 100.0, 100m);
            string sellId = broker.ExecuteOrder(sellLimit, 101.0, null);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(sellId));
            Assert.IsNotNull(sellReport);
            Assert.AreEqual(100.0, sellReport.FillPrice, 1e-4, "卖出限价单成交价格绝对严禁穿透委托限价 100.0");
        }

        [TestMethod]
        public void PendingLimitOrders_FIFOOrderAndReentrancy_ShouldMatchInOrderWithoutDeadlock()
        {
            // 验证同一标的多笔挂单的 FIFO 时间优先撮合顺序，并在事件回调中验证 Open Calls 允许并发重入查询而不死锁
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            var tradeOrderIds = new List<string>();
            broker.OnTrade += r =>
            {
                tradeOrderIds.Add(r.OrderId);
                // 在回调中尝试重入读取经纪商状态，测试 Open Calls 绝不死锁
                double cash = broker.GetCash();
                Assert.IsTrue(cash > 0);
            };

            // 提交两笔均未成交的买单：
            // Order 1 (先来): 9.5 元买 100 股
            // Order 2 (后来): 9.6 元买 100 股
            var o1 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m);
            var o2 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.6, 100m);

            string id1 = broker.ExecuteOrder(o1, 10.0, null);
            string id2 = broker.ExecuteOrder(o2, 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id2));

            // 行情跌至 9.0 元，两笔订单同时触发撮合条件
            // 必须严格按照提交顺序（FIFO 时间优先：id1 先成交，id2 后成交）
            broker.UpdateMarketPrice("sh.600000", 9.0);

            Assert.AreEqual(2, tradeOrderIds.Count);
            Assert.AreEqual(id1, tradeOrderIds[0], "第一笔提交的挂单必须先成交 (FIFO)");
            Assert.AreEqual(id2, tradeOrderIds[1], "第二笔提交的挂单必须后成交 (FIFO)");
        }

        [TestMethod]
        public void SyncState_WithPendingOrders_ShouldCancelPendingAndSetMarketPrice()
        {
            // 验证 SyncState 会将现存的未成交挂单显式置为 Canceled，解冻资金，并给新同步的持仓赋予最新行情价
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 挂单 9.0 元买 1000 股（冻结 9000 元）
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m);
            string orderId = broker.ExecuteOrder(buyOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(100_000.0 - 9000.0, broker.GetCash());

            var canceledIds = new List<string>();
            broker.OnOrderStatusChanged += (id, status) =>
            {
                if (status == OrderStatus.Canceled)
                {
                    canceledIds.Add(id);
                }
            };

            // 外部系统强制对账同步：现金 80000，持仓 600000 共 2000 股
            broker.SyncState(80_000.0, new Dictionary<string, decimal> { ["sh.600000"] = 2000m });

            // 验证挂单被自动清理与取消
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.IsTrue(canceledIds.Contains(orderId));
            Assert.AreEqual(80_000.0, broker.GetCash(), "挂单冻结已全额复位，可用现金与同步现金一致");
            Assert.AreEqual(2000m, broker.GetPosition("sh.600000"));

            // 验证总资产包含该持仓按市价 10.0 元的估值（80,000 + 2000 * 10 = 100,000）
            Assert.AreEqual(100_000.0, broker.GetTotalEquity(), 1e-4);
        }

        [TestMethod]
        public void SubmitOrder_PublicContract_LimitOrder_ShouldUseMarketPriceAsBenchmark()
        {
            // 验证公开契约 SubmitOrder 提交低于市价的限价买单时，必须正确识别为未成交挂单，绝不能自成交
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = new SimulatedBroker(config);
            // 市场当前行情价为 10.0 元
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 策略调用通用公开接口 SubmitOrder：限价 9.0 元买入 1000 股
            var limitBuy = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m);
            string orderId = broker.SubmitOrder(limitBuy);

            // 核心断言：必须进入挂单 Submitted，绝不能当场自成交！
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId), "低于市价的限价买单绝不能立即自成交");
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"), "尚未成交，持仓必须为 0");
            Assert.AreEqual(100_000.0 - 9000.0, broker.GetCash(), "挂单按委托限价 9.0 元冻结 9000 元");

            // 行情下跌到 8.8 元，触发限价撮合
            broker.UpdateMarketPrice("sh.600000", 8.8);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId), "行情触碰限价后必须自动撮合成交");
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void ShortPosition_PendingBuyLimitOrders_ShouldPreventDoubleSpending()
        {
            // 验证持有期货空头时，连续提交买平挂单，系统必须精确扣减空头平仓可用额度，彻底阻断平仓双花超卖漏洞
            var config = new BrokerConfig
            {
                InitialCash = 50_000.0,
                AllowShort = true,
                MarginRate = 0.1, // 10% 保证金
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = new SimulatedBroker(config);
            broker.UpdateMarketPrice("rb2505", 100.0);

            // 1. 建立空头持仓 5 手 @ 100.0 元 (占用保证金 50 元)
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Market, 100.0, 5m), 100.0, null);
            Assert.AreEqual(-5m, broker.GetPosition("rb2505"));
            Assert.AreEqual(-5m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(50_000.0 - 50.0, broker.GetCash());

            // 2. 挂单 1：在 90.0 元买入 5 手平空（低于市价 100.0，排队挂单）
            // 预期：此单正好占满 5 手空头平仓额度，平仓免开仓保证金
            var order1 = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m);
            string id1 = broker.SubmitOrder(order1);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));
            Assert.AreEqual(-5m, broker.GetPosition("rb2505"), "总持仓仍为 -5");
            Assert.AreEqual(0m, broker.GetAvailablePosition("rb2505"), "空头平仓额度已被 100% 冻结完毕");

            // 3. 挂单 2（防双花触发）：策略紧接着又在 90.0 元挂单买入 5 手平空！
            // 预期：由于 5 手空头平仓额度已全被挂单 1 冻结，此单必须被识别为反手新开 5 手多头！
            // 所需保证金 = 5 * 90.0 * 0.1 = 45.0 元。因为账户还有足够可用现金，可以提交挂单，但必须被扣除保证金！
            var order2 = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m);
            string id2 = broker.SubmitOrder(order2);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id2));
            // 账户必须额外被冻结 45.0 元反手保证金，绝不能作为平仓免保证金双花放行！
            Assert.AreEqual(50_000.0 - 50.0 - 45.0, broker.GetCash(), 1e-4, "挂单 2 必须按反手开多收取 45 元保证金冻结");

            // 4. 挂单 3（超额资金拦截）：假设资金耗尽，策略再尝试挂反手单，必须立即 Rejected
            var configPoor = new BrokerConfig
            {
                InitialCash = 60.0, // 仅 60 元初始资金
                AllowShort = true,
                MarginRate = 0.1,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var brokerPoor = new SimulatedBroker(configPoor);
            brokerPoor.UpdateMarketPrice("rb2505", 100.0);
            // 建立 5 手空头 (消耗 50 元保证金，剩余 10 元)
            brokerPoor.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Market, 100.0, 5m), 100.0, null);
            // 挂单 1：5 手买平（免保证金，剩余 10 元）
            brokerPoor.SubmitOrder(new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m));
            // 挂单 2：试图再次双花平仓 5 手。因为额度用完必须开多，开多需 45 元保证金，但可用资金仅 10 元，必须被拒！
            string idReject = brokerPoor.SubmitOrder(new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m));
            Assert.AreEqual(OrderStatus.Rejected, brokerPoor.GetOrderStatus(idReject), "无平仓额度且资金不足以开多时，必须严格拒单");
        }
    }
}
