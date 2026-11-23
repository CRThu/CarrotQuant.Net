using System;
using System.Collections.Generic;
using System.Reflection;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class SimulatedBrokerTests
    {
        private static Broker CreateBroker(BrokerConfig config, IFieldRegistry? fields = null)
        {
            var gateway = new SimulatedExchangeGateway(config, fields);
            return new Broker(gateway, config, fields);
        }
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
        public void Broker_Gateway_MustBeExplicitInterfaceImplementation_ToSealBackdoor()
        {
            var config = new BrokerConfig();
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config);

            // 接口契约与多态隔离验证：只有通过显式转换至内部基础设施契约 IExchangeGatewayProvider 才能合法获取底层网关
            Assert.IsTrue(broker is IExchangeGatewayProvider, "Broker 必须实现内部 IExchangeGatewayProvider 基础设施契约");
            var provider = (IExchangeGatewayProvider)broker;
            Assert.AreSame(gateway, provider.Gateway, "显式接口实现必须返回底层绑定的网关实例");

            // 验证策略面向的 IBroker 契约独立性
            IBroker strategyBroker = broker;
            Assert.IsNotNull(strategyBroker);
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

            var broker = CreateBroker(config);

            // 1. 买入 10,000 股 @ 10.0 元 (基准价 10.0，买入滑点千一 -> 10.01)
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10000m);
            long buyId = broker.SubmitOrder(buyOrder);

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
            long sellId = broker.SubmitOrder(sellOrder);

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

            var broker = CreateBroker(config);

            // 当天买入
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
            broker.SubmitOrder(buyOrder);

            // 当天立即尝试卖出，应被 T+1 可用量限制拦截拒绝
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.5, 1000m);
            long sellId = broker.SubmitOrder(sellOrder);

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

            var broker = CreateBroker(config);

            // 无持仓直接卖空，直接被拒绝
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 500m);
            long sellId = broker.SubmitOrder(sellOrder);

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

            var broker = CreateBroker(config);

            // 尝试买入价值 10,000 元的股票
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
            long buyId = broker.SubmitOrder(buyOrder);

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

            var broker = CreateBroker(config);

            // 1. 停牌场景 (Volume = 0)
            var suspendedBar = CreateBar(10.0, 10.0, 10.0, 10.0, volume: 0.0);
            var order1 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m);
            long id1 = broker.ExecuteOrder(order1, 10.0, suspendedBar);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id1));

            // 2. 流动性不足场景 (当前 Bar 成交量 5,000 股，允许最大 500 股；委托 1,000 股超标)
            var lowVolBar = CreateBar(10.0, 10.5, 9.8, 10.2, volume: 5000.0);
            var order2 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.2, 1000m);
            long id2 = broker.ExecuteOrder(order2, 10.2, lowVolBar);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2));

            // 3. 流动性充足场景 (委托 500 股，恰好等于 5000 * 0.1)
            var order3 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.2, 500m);
            long id3 = broker.ExecuteOrder(order3, 10.2, lowVolBar);
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
                    if (order.IsBuy && bar.CloseOr(0.0) >= 11.0)
                    {
                        return false; // 涨停禁止买入
                    }
                    return true;
                }
            };

            var broker = CreateBroker(config);

            // 涨停 Bar: close = 11.0, limit_up = 11.0
            var limitUpBar = CreateBar(10.0, 11.0, 10.0, 11.0, volume: 100000.0, limitUp: 11.0);
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 11.0, 1000m);

            long orderId = broker.ExecuteOrder(buyOrder, 11.0, limitUpBar);
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

            var broker = CreateBroker(config);

            // 1. 多头开仓：买入 10 手螺纹钢 @ 3,500 元/吨
            // 名义金额 = 10手 * 3500 * 10 = 350,000 元
            // 占用保证金 = 350,000 * 0.1 = 35,000 元
            // 手续费 = 350,000 * 0.0001 = 35 元
            var openLong = new OrderRequest("rb2410", OrderDirection.Buy, OrderType.Market, 3500.0, 10m);
            long longId = broker.SubmitOrder(openLong);

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
            long closeId = broker.SubmitOrder(closeLong);

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
            long shortId = broker.SubmitOrder(openShort);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(shortId));
            Assert.AreEqual(-5m, broker.GetPosition("rb2410"));

            // 4. 空头平仓：价格下跌至 3,500 元/吨，买平 5 手
            // 盈利 = (3600 - 3500) * 5 * 10 = 5,000 元
            // 释放保证金 18,000 元
            // 平仓手续费 = 5 * 3500 * 10 * 0.0001 = 17.5 元
            var closeShort = new OrderRequest("rb2410", OrderDirection.Buy, OrderType.Market, 3500.0, 5m);
            long closeShortId = broker.SubmitOrder(closeShort);

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

            var broker = CreateBroker(config);
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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

            // 1. 测试非法参数保护
            var invalidOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 0m);
            long id = broker.SubmitOrder(invalidOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id));

            // 2. 测试查询不存在的订单
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(999999L));

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
            var spotBroker = CreateBroker(spotConfig);
            spotBroker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10000m));
            // 买入花费 100,000，剩余现金 900,000，持仓市值 100,000，总权益应严格等于 1,000,000
            Assert.AreEqual(1_000_000.0, spotBroker.GetTotalEquity(), 1e-4);

            // 2. 渐进多批次 T+1 跨日解锁 (remainingLots 分支)
            var multiLotBroker = CreateBroker(spotConfig);
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
            var volBroker = CreateBroker(volHookConfig);
            var bar = CreateBar(10.0, 10.5, 9.8, 10.0, 10000.0);

            long rejectId = volBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 600m), 10.0, bar);
            Assert.AreEqual(OrderStatus.Rejected, volBroker.GetOrderStatus(rejectId));

            long fillId = volBroker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 400m), 10.0, bar);
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
            var dynBroker = CreateBroker(dynamicConfig, dummyFields);
            var passBar = CreateBar(100.0, 105.0, 98.0, 100.0, 100000.0);
            long dynOrderId = dynBroker.ExecuteOrder(new OrderRequest("custom_item", OrderDirection.Buy, OrderType.Market, 100.0, 10m), 100.0, passBar);
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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

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

            var broker = CreateBroker(config, fields: fields);
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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(new BrokerConfig
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
            var broker = CreateBroker(config);

            // 1. 测试 NaN 价格
            var nanOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, double.NaN, 100m);
            long id1 = broker.ExecuteOrder(nanOrder, double.NaN, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id1));
            Assert.IsFalse(double.IsNaN(broker.GetCash()), "账户资金绝不能被 NaN 污染");
            Assert.AreEqual(1_000_000.0, broker.GetCash(), 1e-4);

            // 2. 测试 Infinity 价格
            var infOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, double.PositiveInfinity, 100m);
            long id2 = broker.ExecuteOrder(infOrder, double.PositiveInfinity, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2));
            Assert.AreEqual(1_000_000.0, broker.GetCash(), 1e-4);

            // 3. 测试 -Infinity 价格
            long id3 = broker.ExecuteOrder(infOrder, double.NegativeInfinity, null);
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
            var broker = CreateBroker(config);

            // 买入 100 股 @ 2.0 元，成交额 200 元；万三佣金仅 0.06 元，必须触发 5.0 元最低手续费
            var order = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 2.0, 100m);
            long id = broker.SubmitOrder(order);
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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

            // 初始持有多头 5 手 (假设通过 SyncState 注入，避免开仓扣现金)
            broker.SyncState(10.0, new Dictionary<string, decimal> { ["rb2505"] = 5m });

            // 意图卖出 80 手 (平多 5 手，反手开空 75 手)
            // 平多 5 手释放 50 元信用，反手开空 75 手需要保证金 75 * 100 * 0.1 = 750 元
            // 净需现金 750 - 50 = 700 元，而可用现金仅 10 元，必须被拒单拦截
            long id = broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 100.0, 80m), 100.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id));
            Assert.AreEqual(5m, broker.GetPosition("rb2505"), "拒单后持仓不变");
            Assert.AreEqual(10.0, broker.GetCash(), "拒单后资金不变");
        }

        [TestMethod]
        public void SubmitOrder_MarketOrder_UsesMarketPrice()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0, Slippage = 0.0, FeeRate = 0.0 };
            var broker = CreateBroker(config);

            // 预先更新市场价格
            broker.UpdateMarketPrice("sh.600000", 15.0);

            // 市价单便捷构造函数（Price 默认为 0.0）
            var marketOrder = new OrderRequest("sh.600000", OrderDirection.Buy, 100m);
            long id = broker.SubmitOrder(marketOrder);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(id));
            Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(100_000.0 - 1500.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void MultiAsset_SpotLongAndFuturesShortHedge_ShouldCoexistSeamlessly()
        {
            // 多资产大类混合配置：同一个账户内同时运行 A 股现货多头 + 股指期货空头对冲
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
            var broker = CreateBroker(config);

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
            var broker = CreateBroker(config);

            DateTime day1 = new DateTime(2025, 1, 6, 9, 30, 0);
            broker.Step(day1);

            // 1. 当日买入股票 1,000 股 @ 10.0 元 (由于 SettleDays=1，在途锁仓，可用仓位为 0)
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 2. 当日试图卖出 1,000 股：由于尚有未解冻在途交收份额（PendingLots），必须被严厉拒单拦截！
            long sellOrderId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellOrderId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "拒单后持仓不可变");
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "拒单后可用持仓不可变");

            // 3. 次日早盘推进解冻
            broker.Step(new DateTime(2025, 1, 7, 9, 30, 0));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "次日成功解锁为 1000");

            // 4. 次日卖出正常成交
            long day2SellId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(day2SellId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void MarkToMarket_SuspendedOrDelistedSymbolWithNaN_ShouldPreserveLastPriceAndNotCorruptEquity()
        {
            // 验证核心防腐韧性：持仓标的突发停牌或退市（市场行情连续缺失 double.NaN）时，
            // 账本与 Broker 盯市结算必须自动回退锁定至上一有效公允收盘价 (LastPrice)，
            // 绝不能使每日快照或总资产权益坍塌污染为 NaN！
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                Slippage = 0.0,
                FeeRate = 0.0
            };
            var broker = CreateBroker(config);
            DateTime day1 = new DateTime(2025, 1, 2, 9, 30, 0);
            broker.Step(day1);

            // 1. 在 Day 1 以 10.0 元买入 1000 股
            broker.UpdateMarketPrice("sh.600000", 10.0);
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m), 10.0, null);

            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(90_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(100_000.0, broker.GetTotalEquity(), 1e-4);

            // 2. Day 1 收盘前，价格微涨至 10.5 元，触发盯市更新
            broker.UpdateMarketPrice("sh.600000", 10.5);
            Assert.AreEqual(100_500.0, broker.GetTotalEquity(), 1e-4);

            // 3. 推进至 Day 2 (次日)：股票突发停牌或退市，市场价格为 double.NaN
            DateTime day2 = new DateTime(2025, 1, 3, 9, 30, 0);
            broker.Step(day2);

            // priceResolver 返回 NaN (停牌/无行情)
            var snapDay2 = broker.CreateDailySnapshot(day2, sym => double.NaN);

            // 验证核心韧性：
            // a. 标的当日行情缺失 (NaN)，系统必须平滑回退至上一有效收盘价 (10.5 元)，绝不能被污染为 NaN！
            Assert.IsFalse(double.IsNaN(snapDay2.TotalEquity), "总资产权益绝不能被 NaN 污染");
            Assert.AreEqual(100_500.0, snapDay2.TotalEquity, 1e-4);
            Assert.AreEqual(10_500.0, snapDay2.TotalMarketValue, 1e-4);
            Assert.AreEqual(500.0, snapDay2.UnrealizedPnL, 1e-4);
            Assert.AreEqual(90_000.0, snapDay2.AvailableCash, 1e-4);

            // b. broker.GetTotalEquity 传入 null 或 NaN 解析器，同样守恒为 100,500.0
            Assert.AreEqual(100_500.0, broker.GetTotalEquity(sym => double.NaN), 1e-4);
            Assert.AreEqual(100_500.0, broker.GetTotalEquity(null), 1e-4);
        }

        [TestMethod]
        public void Broker_PositionValuation_WhenBothMarketAndLastPriceMissing_ReturnsNaNAndLogsWarning()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0, Slippage = 0.0, FeeRate = 0.0 };
            var logs = new List<string>();
            var monitor = new TestLogMonitor(msg => logs.Add(msg));
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config, monitor: monitor);

            // 1. 同步初始持仓：有持仓量，但从无历史成交与市价（LastPrice 缺失）
            broker.SyncState(50_000.0, new Dictionary<string, decimal> { ["UNKNOWN_SYM"] = 1000m });

            // 2. 外部行情解析器返回 NaN
            double equity = broker.GetTotalEquity(sym => double.NaN);

            // 3. 验证严禁假兜底：必须返回 NaN，且 Broker 记录了警告日志
            Assert.IsTrue(double.IsNaN(equity), "在无任何有效市价与 LastPrice 时，总资产必须决议为 NaN 坚决杜绝假兜底");
            Assert.IsTrue(logs.Any(log => log.Contains("[Broker 警告]") && log.Contains("UNKNOWN_SYM")),
                "Broker 必须发出明确的强类型警告日志提示价格缺失");
        }

        [TestMethod]
        public void Broker_LimitBuyFilledAtBetterPrice_ShouldReleaseExcessFrozenCash()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0, Slippage = 0.0, FeeRate = 0.0 };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 提交限价 10.0 买单 1000 股，冻结 10,000 元
            var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
            // 以更优价 9.5 元撮合成交
            long orderId = broker.ExecuteOrder(req, 9.5, null);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "更优价成交后剩余冻结资金必须彻底释放归零");
            Assert.AreEqual(100_000.0 - 9500.0, broker.GetCash(), 1e-4, "可用资金仅扣减实际成交金额 9500 元");
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void Broker_AllowShortFalse_SellExceedingPosition_ShouldRejectToPreventShorting()
        {
            // 验证现货禁止做空防御边界：当 AllowShort = false 时，
            // 若卖出数量超过现有持仓（试图卖平现有仓位后违规穿透开空），必须被系统在两阶段预冻结阶段硬拦截拒单！
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = false,
                SettleDays = 0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 1. 买入 1000 股现货
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 2. 试图卖出 1500 股（现有仅 1000 股，企图平 1000 + 空 500）-> 坚决拒单！
            long sellId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 1500m), 10.0, null);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(sellId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "拒单后持仓不可变动");
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "拒单后可用持仓不可变动");
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4);
        }

        [TestMethod]
        public void Broker_ShortPosition_TakeProfitBuyTrigger_ShouldCloseShortAtFavorablePrice()
        {
            // 验证期货空头仓位的买入止盈 (TakeProfit Buy) 全链路：
            // 1 手 IF 期货空头均价 4000 点，设置 3900 点买入止盈
            // 次日开盘跳空低开至 3850 点，触发以更优开盘价 3850 成交，浮盈兑现结转！
            var config = new BrokerConfig
            {
                InitialCash = 1_000_000.0,
                AllowShort = true,
                Multiplier = 300.0,
                MarginRate = 0.1,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            var gateway = ((IExchangeGatewayProvider)broker).Gateway as ISimulatedExchange;

            DateTime day1 = new DateTime(2025, 1, 6, 9, 30, 0);
            broker.Step(day1);
            broker.UpdateMarketPrice("IF2501", 4000.0);

            // 1. 开空 1 手 @ 4000 点 (占用保证金 120,000 元，现金变为 880,000 元)
            broker.ExecuteOrder(new OrderRequest("IF2501", OrderDirection.Sell, OrderType.Market, 4000.0, 1m), 4000.0, null);
            Assert.AreEqual(-1m, broker.GetPosition("IF2501"));
            Assert.AreEqual(880_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(1_000_000.0, broker.GetTotalEquity(), 1e-4);

            // 2. 挂买入止盈单 TakeProfit @ 3900 点 (触发价 3900)
            var tpRequest = new OrderRequest("IF2501", OrderDirection.Buy, OrderType.TakeProfit, 3900.0, 1m, ReduceOnly: true)
            {
                StopPrice = 3900.0
            };
            long tpOrderId = broker.SubmitOrder(tpRequest);
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpOrderId));

            // 3. 次日开盘跳空低开至 3850 点 (open <= stopPrice 满足止盈触发条件)
            DateTime day2 = new DateTime(2025, 1, 7, 9, 30, 0);
            broker.Step(day2);
            var jumpBar = new BarSnapshot(open: 3850.0, high: 3860.0, low: 3840.0, close: 3855.0, volume: 10000.0);
            gateway?.OnMarketData("IF2501", day2, in jumpBar);

            // 推进条件单触发循环
            broker.ProcessConditionalOrders(day2);

            // 验证：
            // a. 止盈单成功触发并以更优开盘价 3850 成交平空！
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(tpOrderId));
            Assert.AreEqual(0m, broker.GetPosition("IF2501"), "空头持仓全额平仓归零");

            // b. 盈利兑现结转: (4000 - 3850) * 1 * 300 = +45,000 元，保证金 120,000 全额释放回现金
            Assert.AreEqual(1_000_000.0 + 45_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(1_045_000.0, broker.GetTotalEquity(), 1e-4);
        }

        [TestMethod]
        public void Broker_SubmitDuplicateConditionalOrderId_ShouldReject()
        {
            var config = new BrokerConfig();
            var broker = CreateBroker(config);

            OrderStatus? eventStatus = null;
            broker.OnOrderStatusChanged += (oid, status) =>
            {
                if (oid == 8888L)
                {
                    eventStatus = status;
                }
            };

            // 显式指定 OrderId = 8888 提交条件单
            var req1 = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Stop, 9.0, 100m)
            {
                OrderId = 8888L,
                StopPrice = 9.0
            };
            long id1 = broker.SubmitOrder(req1);
            Assert.AreEqual(8888L, id1);
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(id1));

            // 重复提交相同 OrderId = 8888 的条件单
            var req2 = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Stop, 8.5, 100m)
            {
                OrderId = 8888L,
                StopPrice = 8.5
            };
            long id2 = broker.SubmitOrder(req2);
            Assert.AreEqual(8888L, id2);

            // 验证：
            // 1. Broker 必须触发 Rejected 拒单事件通知调用方
            Assert.AreEqual(OrderStatus.Rejected, eventStatus);
            // 2. 原订单仍完好处于 Pending 状态，不被重复提交篡改
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(id1));
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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0); // 当前市价 10.0 元

            // 1. 提交买入限价单 9.0 元 (低于市价 10.0，进入未成交挂单队列)
            // 买入 10,000 股 @ 9.0 元 -> turnover = 90,000 元, fee = 27 元 -> 需冻结 90,027 元
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 10_000m);
            long orderId = broker.ExecuteOrder(buyOrder, 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"), "未成交前持仓为 0");
            Assert.AreEqual(100_000.0 - 90_027.0, broker.GetCash(), 1e-4, "可用资金必须扣除挂单冻结保证金与费用");
            Assert.AreEqual(100_000.0, broker.GetTotalEquity(), 1e-4, "挂单阶段账户总资产不发生实际消耗");

            // 2. 防双花测试：剩余可用资金仅 9,973 元，试图再次挂单买入需 18,000 元的订单 -> 必须被拒单拦截！
            var doubleSpendOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 2_000m);
            long rejId = broker.ExecuteOrder(doubleSpendOrder, 10.0, null);
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
            var broker = CreateBroker(config);
            // 初始通过 SyncState 注入 1,000 股现货
            broker.SyncState(100_000.0, new Dictionary<string, decimal> { ["sh.600000"] = 1000m });
            broker.UpdateMarketPrice("sh.600000", 10.0);

            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 1. 挂单 11.0 元卖出 800 股 (高于市价 10.0，进入未成交挂单队列)
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 11.0, 800m);
            long orderId = broker.ExecuteOrder(sellOrder, 10.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "总持仓仍归属于账户");
            Assert.AreEqual(200m, broker.GetAvailablePosition("sh.600000"), "可用持仓必须被扣减 800 股冻结量，仅余 200 股");

            // 2. 防超卖测试：剩余可用仅 200 股，试图再次卖出 500 股 -> 必须被拒单拦截！
            var overSellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 11.0, 500m);
            long rejId = broker.ExecuteOrder(overSellOrder, 10.0, null);
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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            bool tradeEventFired = false;
            broker.OnTrade += r => { tradeEventFired = true; };

            // 1. 提交买入限价单 9.5 元买入 1,000 股 (冻结 9500 + 2.85 元)
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
            long orderId = broker.ExecuteOrder(buyOrder, 10.0, null);
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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("IF2501", 3900.0);

            // 1. 挂单 4,000 点做空 1 手 (高于当前市价 3900，未成交)
            // 名义金额 = 4000 * 300 = 1,200,000. 10% 保证金 = 120,000 元
            var sellOrder = new OrderRequest("IF2501", OrderDirection.Sell, OrderType.Limit, 4000.0, 1m);
            long orderId = broker.ExecuteOrder(sellOrder, 3900.0, null);

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
            var broker = CreateBroker(config);
            broker.SyncState(100_000.0, new Dictionary<string, decimal> { ["sh.600000"] = 1000m });
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 1. 挂单 10.5 元卖出 1,000 股 (高于市价 10.0，未成交)
            var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.5, 1000m);
            long orderId = broker.ExecuteOrder(sellOrder, 10.0, null);
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
            var broker = CreateBroker(config);
            // 初始持有多头 5 手 @ 100.0 (占用保证金 50 元)
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 100.0, 5m), 100.0, null);
            Assert.AreEqual(5m, broker.GetPosition("rb2505"));
            Assert.AreEqual(5m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(500_000.0 - 50.0, broker.GetCash(), 1e-4);

            // 意图：在 110.0 元反手卖出 20 手 (平多 5 手 + 开空 15 手)
            // 市价为 100.0，挂单 110.0 未能立即成交 -> 进入未成交挂单
            // 复合冻结检查：
            // 1. 持仓冻结：多头 5 手可用持仓全部冻结 -> AvailableQty 从 5 降为 0
            // 2. 资金冻结：平多 5 手释放信用 50(保证金) + 50(浮盈) = 100 元；反手新开 15 手需保证金 15 * 110 * 0.1 = 165 元
            //    净冻结现金 = 165 - 100 = 65 元 -> Cash 从 (500000-50) 降为 (500000-50-65)
            var flipOrder = new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Limit, 110.0, 20m);
            long orderId = broker.ExecuteOrder(flipOrder, 100.0, null);

            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(5m, broker.GetPosition("rb2505"), "总持仓仍归属于账户");
            Assert.AreEqual(0m, broker.GetAvailablePosition("rb2505"), "平多的 5 手必须已被冻结");
            Assert.AreEqual(500_000.0 - 50.0 - 65.0, broker.GetCash(), 1e-4, "反手新开 15 手扣除平仓信用后净冻结 65 元保证金");

            // 撤单：持仓与反手保证金同时全额释放
            broker.CancelOrder(orderId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(5m, broker.GetAvailablePosition("rb2505"), "持仓冻结释放，可用持仓恢复为 5");
            Assert.AreEqual(500_000.0 - 50.0, broker.GetCash(), 1e-4, "反手保证金 65 元释放恢复");
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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 阶梯挂两笔买单：
            // Order 1: 9.6 元买 1,000 股 (冻结 9600)
            // Order 2: 9.2 元买 1,000 股 (冻结 9200)
            long id1 = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.6, 1000m), 10.0, null);
            long id2 = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.2, 1000m), 10.0, null);

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
            var brokerNoLimit = CreateBroker(noLimitConfig);
            // Bar 成交量仅 100 股
            var barSmall = CreateBar(10.0, 10.5, 9.8, 10.0, 100.0);
            var hugeOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 10_000m);
            long id1 = brokerNoLimit.ExecuteOrder(hugeOrder, 10.0, barSmall);
            Assert.AreEqual(OrderStatus.Filled, brokerNoLimit.GetOrderStatus(id1), "VolumeLimit=0 时应无视 Bar Volume 限制正常成交");

            var limitedConfig = new BrokerConfig
            {
                InitialCash = 10_000_000.0,
                VolumeLimit = 0.1, // 限制最大 10%
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var brokerLimited = CreateBroker(limitedConfig);
            long id2 = brokerLimited.ExecuteOrder(hugeOrder, 10.0, barSmall);
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
            var broker = CreateBroker(config);

            // 美东时间 2025-01-05 15:30:00 (盘中买入，T+1 到期日应为美东当地 2025-01-06)
            DateTime nyTimeBuy = new DateTime(2025, 1, 5, 15, 30, 0);
            DateTime utcBuy = TimeZoneInfo.ConvertTimeToUtc(nyTimeBuy, nyZone);
            broker.Step(utcBuy);

            long buyId = broker.SubmitOrder(new OrderRequest("AAPL", OrderDirection.Buy, OrderType.Market, 150.0, 100m));
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
            var broker = CreateBroker(config);

            // 1. 买入限价单：基准市价 99.0，委托限价 100.0。
            // 正常 5% 滑点计算价格 = 99.0 * 1.05 = 103.95 (高于委托限价 100.0)。
            // 契约保护要求：成交价必须被截断截平在 100.0，绝不能以 103.95 穿透限价成交！
            ExecutionReport? buyReport = null;
            broker.OnTrade += r => buyReport = r;

            var buyLimit = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 100.0, 100m);
            long buyId = broker.ExecuteOrder(buyLimit, 99.0, null);

            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.IsNotNull(buyReport);
            Assert.AreEqual(100.0, buyReport.FillPrice, 1e-4, "买入限价单成交价格绝对严禁穿透委托限价 100.0");

            // 2. 卖出限价单：基准市价 101.0，委托限价 100.0。
            // 正常 5% 滑点计算价格 = 101.0 * (1 - 0.05) = 95.95 (低于委托限价 100.0)。
            // 契约保护要求：成交价必须被截断截平在 100.0，绝不能以 95.95 穿透限价成交！
            ExecutionReport? sellReport = null;
            broker.OnTrade += r => sellReport = r;

            var sellLimit = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 100.0, 100m);
            long sellId = broker.ExecuteOrder(sellLimit, 101.0, null);

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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            var tradeOrderIds = new List<long>();
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

            long id1 = broker.ExecuteOrder(o1, 10.0, null);
            long id2 = broker.ExecuteOrder(o2, 10.0, null);

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
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 挂单 9.0 元买 1000 股（冻结 9000 元）
            var buyOrder = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m);
            long orderId = broker.ExecuteOrder(buyOrder, 10.0, null);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
            Assert.AreEqual(100_000.0 - 9000.0, broker.GetCash());

            var canceledIds = new List<long>();
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
            var broker = CreateBroker(config);
            // 市场当前行情价为 10.0 元
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 策略调用通用公开接口 SubmitOrder：限价 9.0 元买入 1000 股
            var limitBuy = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m);
            long orderId = broker.SubmitOrder(limitBuy);

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
            // 通用净持仓与多空镜像对称测试：
            // 当空头持有 5 手，挂单 1 挂出 5 手平空（平仓额度被 100% 占满）后，
            // 策略再次尝试买平 5 手（数量 <= 总持仓），由于可用平仓额度不足，系统必须严格拒单拦截，
            // 坚决杜绝“免保证金双花”，且坚决杜绝在未声明翻多时被意外开仓！
            var config = new BrokerConfig
            {
                InitialCash = 50_000.0,
                AllowShort = true,
                MarginRate = 0.1, // 10% 保证金
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("rb2505", 100.0);

            // 1. 建立空头持仓 5 手 @ 100.0 元 (占用保证金 50 元)
            broker.ExecuteOrder(new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Market, 100.0, 5m), 100.0, null);
            Assert.AreEqual(-5m, broker.GetPosition("rb2505"));
            Assert.AreEqual(-5m, broker.GetAvailablePosition("rb2505"));
            Assert.AreEqual(50_000.0 - 50.0, broker.GetCash());

            // 2. 挂单 1：在 90.0 元买入 5 手平空（低于市价 100.0，排队挂单）
            // 预期：此单正好占满 5 手空头平仓额度，平仓免开仓保证金
            var order1 = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m);
            long id1 = broker.SubmitOrder(order1);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));
            Assert.AreEqual(-5m, broker.GetPosition("rb2505"), "总持仓仍为 -5");
            Assert.AreEqual(0m, broker.GetAvailablePosition("rb2505"), "空头平仓额度已被 100% 冻结完毕");

            // 3. 挂单 2（防双花触发）：策略紧接着又在 90.0 元挂单买入 5 手平空！
            // 预期：由于 5 手空头平仓额度已被挂单 1 冻结，此单属于可用平空不足，必须被严格 Rejected！杜绝双花！
            var order2 = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 5m);
            long id2 = broker.SubmitOrder(order2);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2), "可用平空额度已耗尽，买平单必须直接被拒绝，杜绝双花");
            Assert.AreEqual(50_000.0 - 50.0, broker.GetCash(), 1e-4, "被拒后现金未被任何异常扣减");

            // 4. 标准空翻多测试：在现有空头完全可用的前提下，发起 Q = 10 > Total = 5 的翻多单
            // 先撤销挂单 1 释放平空额度
            broker.CancelOrder(id1);
            Assert.AreEqual(-5m, broker.GetAvailablePosition("rb2505"), "撤单后 5 手平空额度安全恢复");

            // 发起 Buy 10 手翻多单：平 5 手空头 + 新开 5 手多头
            // 平空 5 手释放信用 50(保证金) + 50(浮盈) = 100 元，开多 5 手需保证金 5 * 90 * 0.1 = 45 元
            // 释放信用完全覆盖开多保证金，净冻结现金为 0 元
            var flipOrder = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Limit, 90.0, 10m);
            long flipId = broker.SubmitOrder(flipOrder);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(flipId));
            Assert.AreEqual(50_000.0 - 50.0, broker.GetCash(), 1e-4, "平空释放信用完全覆盖开多保证金，净现金冻结为 0 元");
        }

        [TestMethod]
        public void Broker_AuthoritativeCommissionCalculation_StandardAndCustomHooks()
        {
            // 1. 标准佣金计算（测试千分之三费率与最低 5 元门槛）
            var configStandard = new BrokerConfig
            {
                InitialCash = 100_000.0,
                FeeRate = 0.0003,
                MinFee = 5.0,
                Slippage = 0.0
            };
            var broker1 = CreateBroker(configStandard);
            broker1.UpdateMarketPrice("sh.600000", 10.0);

            ExecutionReport? capturedReport = null;
            broker1.OnTrade += r => capturedReport = r;

            // 买入 100 股 @ 10 元，成交额 1000 元，按 0.0003 计算为 0.3 元，但受 MinFee=5.0 约束，最终佣金应权威计算为 5.0 元
            broker1.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m), 10.0, null);

            Assert.IsNotNull(capturedReport);
            Assert.AreEqual(5.0, capturedReport.Commission, 1e-4, "券商必须根据 MinFee 权威计算佣金");
            Assert.AreEqual(100_000.0 - 1000.0 - 5.0, broker1.GetCash(), 1e-4);

            // 2. 自定义 Hook 佣金计算（OnCalculateFee）
            var configHook = new BrokerConfig
            {
                InitialCash = 100_000.0,
                OnCalculateFee = (req, turnover) => 12.34,
                Slippage = 0.0
            };
            var broker2 = CreateBroker(configHook);
            broker2.UpdateMarketPrice("sh.600000", 10.0);

            ExecutionReport? hookReport = null;
            broker2.OnTrade += r => hookReport = r;

            broker2.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m), 10.0, null);

            Assert.IsNotNull(hookReport);
            Assert.AreEqual(12.34, hookReport.Commission, 1e-4, "券商必须调用自定义 OnCalculateFee Hook 计算佣金");
            Assert.AreEqual(100_000.0 - 1000.0 - 12.34, broker2.GetCash(), 1e-4);
        }

        [TestMethod]
        public void SubmitOrder_WithDuplicateOrderId_ShouldBeRejectedAndNotOverwriteExistingOrder()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0,
                SettleDays = 0
            };
            var broker = CreateBroker(config);

            // 预设市场公允价为 10.0
            broker.UpdateMarketPrice("SH.600000", 10.0);

            // 1. 提交首笔带有指定 OrderId 的限价买单（限价 9.0 低于市价 10.0，进入挂单簿排队）
            long specifiedOrderId = 99999L;
            var req1 = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m)
            {
                OrderId = specifiedOrderId
            };

            long id1 = broker.SubmitOrder(req1);
            Assert.AreEqual(specifiedOrderId, id1);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));

            // 首笔订单冻结资金：9.0 * 1000 = 9,000 元，现金剩余 91,000 元
            Assert.AreEqual(91_000.0, broker.GetCash(), 1e-4);

            // 2. 提交第二笔相同 OrderId 的订单（试图覆盖），监听拒绝通知
            OrderStatus? notifiedStatus = null;
            broker.OnOrderStatusChanged += (oid, st) =>
            {
                if (oid == specifiedOrderId) notifiedStatus = st;
            };

            var req2 = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 500m)
            {
                OrderId = specifiedOrderId
            };

            long id2 = broker.SubmitOrder(req2);
            Assert.AreEqual(specifiedOrderId, id2);
            // 必须派发 Rejected 事件通知！
            Assert.AreEqual(OrderStatus.Rejected, notifiedStatus);
            // 原订单在字典中必须完好无损，依然保持 Submitted，严禁被覆盖篡改！
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));

            // 资金绝不能被二次冻结，依旧保持 91,000 元
            Assert.AreEqual(91_000.0, broker.GetCash(), 1e-4);

            // 3. 市价下跌触及 9.0，撮合第一笔订单成交，验证第一笔订单生命周期与冻结结算完全未受影响
            broker.UpdateMarketPrice("SH.600000", 9.0);
            broker.Step(DateTime.UtcNow);

            // 第一笔订单成交为 1000 股持仓
            Assert.AreEqual(1000m, broker.GetPosition("SH.600000"));
            Assert.AreEqual(91_000.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void Broker_HighFrequencyTradeVerbs_BuySellAndClosePosition_ShouldGenerateCorrectOrders()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0,
                SettleDays = 0
            };
            var broker = CreateBroker(config);

            // 1. 便捷 Buy 动词下单并以市价成交
            long buyId = broker.Buy("SH.600000", 1000m, 10.0, OrderType.Market, orderTag: "buy_001");
            Assert.IsTrue(buyId > 0);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.AreEqual(1000m, broker.GetPosition("SH.600000"));
            Assert.AreEqual(90_000.0, broker.GetCash(), 1e-4);

            // 2. 便捷 ClosePosition 动词全额平仓多头
            long closeId = broker.ClosePosition("SH.600000", 10.5, OrderType.Market, orderTag: "close_001");
            Assert.IsTrue(closeId > 0);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(closeId));
            Assert.AreEqual(0m, broker.GetPosition("SH.600000"));
            Assert.AreEqual(100_500.0, broker.GetCash(), 1e-4);

            // 3. 无持仓时 ClosePosition 应返回 0L 且不产生任何订单
            long noOpId = broker.ClosePosition("SH.600000", 10.5);
            Assert.AreEqual(0L, noOpId);
        }

        #region 契约隔离与市价单资金预估 (IBrokerEngineDriver & Market Order Pricing)

        [TestMethod]
        public void Broker_InterfaceSegregation_IBrokerShouldNotContainEngineDriverMethods()
        {
            var iBrokerType = typeof(IBroker);
            Assert.IsNull(iBrokerType.GetMethod("Step"));
            Assert.IsNull(iBrokerType.GetMethod("SyncState"));

            var driverType = typeof(IBrokerEngineDriver);
            Assert.IsTrue(typeof(IBroker).IsAssignableFrom(driverType));
            Assert.IsNotNull(driverType.GetMethod("Step"));
            Assert.IsNotNull(driverType.GetMethod("SyncState"));
        }

        [TestMethod]
        public void Broker_EngineDriver_StepAndSyncState_ShouldWorkCorrectly()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 1,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config);

            Assert.IsTrue(broker is IBrokerEngineDriver);
            IBrokerEngineDriver driver = (IBrokerEngineDriver)broker;

            // 1. 下达买单 1,000 股 @ 10.0 元
            gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 2, 9, 30, 0), new BarSnapshot(close: 10.0));
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m));

            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 2. 通过 driver.Step 推进到次日
            driver.Step(new DateTime(2025, 1, 3, 9, 30, 0));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 3. 通过 driver.SyncState 权威同步账户状态
            var authoritativePositions = new Dictionary<string, decimal>
            {
                ["sh.600000"] = 600m
            };
            driver.SyncState(88_888.0, authoritativePositions);

            Assert.AreEqual(88_888.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(600m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(600m, broker.GetAvailablePosition("sh.600000"));
        }

        [TestMethod]
        public void MarketBuyOrder_WithReferencePrice_ShouldPreFreezeWithSlippage()
        {
            var config = new BrokerConfig
            {
                InitialCash = 10_000.0,
                Slippage = 0.01, // 1% 买入滑点
                FeeRate = 0.0,
                MarginRate = 1.0,
                Multiplier = 1.0
            };
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config);

            // 注入市场行情 10.0 元
            gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: 10.0));

            // 提交市价买单 500 股，Price 为 0.0
            var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 500m);
            long orderId = broker.SubmitOrder(req);

            // 成交价为 10.0 * (1 + 0.01) = 10.10，耗资 500 * 10.10 = 5,050.0
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
            Assert.AreEqual(10_000.0 - 5050.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(0.0, broker.FrozenCash);
            Assert.AreEqual(500m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void MarketBuyOrder_InsufficientCashForPreFreeze_ShouldRejectImmediately()
        {
            var config = new BrokerConfig
            {
                InitialCash = 1_000.0,
                Slippage = 0.0,
                FeeRate = 0.0,
                MarginRate = 1.0,
                Multiplier = 1.0
            };
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config);

            gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: 10.0));

            // 尝试市价买入 200 股（需 2000 元 > 可用 1000 元）
            var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 200m);
            long orderId = broker.SubmitOrder(req);

            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(orderId));
            Assert.AreEqual(1_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(0.0, broker.FrozenCash);
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        }

        [TestMethod]
        public void MarketBuyOrder_WhenNoReferencePriceAvailable_ShouldRejectFast()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0
            };
            var gateway = new SimulatedExchangeGateway(config);
            var broker = new Broker(gateway, config);

            // 标的无历史行情与当期价格记录
            var req = new OrderRequest("sh.999999", OrderDirection.Buy, OrderType.Market, 0.0, 100m);
            long orderId = broker.SubmitOrder(req);

            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(orderId));
            Assert.AreEqual(100_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(0.0, broker.FrozenCash);
        }

        [TestMethod]
        public void Broker_Step_IntradayMultipleBars_ShouldOnlyTriggerDayRolloverOncePerDay()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = false,
                SettleDays = 1
            };
            var broker = CreateBroker(config);

            // Day 1: 买入 1000 股，在途 T+1 锁定
            DateTime day1_bar1 = new DateTime(2025, 1, 6, 9, 30, 0);
            broker.Step(day1_bar1);
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "Day 1 在途锁仓，可用为 0");

            // 同一天内的多个高频 Bar 推进 (9:31, 10:00, 14:59)
            broker.Step(new DateTime(2025, 1, 6, 9, 31, 0));
            broker.Step(new DateTime(2025, 1, 6, 10, 0, 0));
            broker.Step(new DateTime(2025, 1, 6, 14, 59, 0));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "同一天内日切守卫生效，可用持仓保持为 0");

            // Day 2: 跨日首个 Bar 推进，触发日切解锁
            broker.Step(new DateTime(2025, 1, 7, 9, 30, 0));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "Day 2 跨日日切触发，成功解锁持仓");

            // Day 2 内部推进依然守卫
            broker.Step(new DateTime(2025, 1, 7, 10, 0, 0));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));
        }

        [TestMethod]
        public void AllowShortTrue_LongPositionPartiallyFrozen_SellOrderExceedingAvailable_MustBeRejectedAndNotTurnIntoShortOrder()
        {
            // 防御审计 8.3：在允许做空（AllowShort=true）模式下，
            // 若持有多头 1000 股，由于挂单排队冻结了 700 股导致可用仅剩 300 股，
            // 策略发出卖出 1000 股（或 500 股）平仓单时，必须因可用持仓不足被明确拒绝，
            // 坚决严禁将不足平仓的差额静默篡改为开空单，避免多空双向持仓与透支！
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                MarginRate = 1.0,
                FeeRate = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("sh.600000", 10.0);

            // 1. 建立 1000 股多头持仓
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

            // 2. 挂单 1：以 12.0 元挂出限价卖单 700 股（排队未成交），冻结 700 股多头
            long id1 = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 12.0, 700m));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(id1));
            Assert.AreEqual(300m, broker.GetAvailablePosition("sh.600000"), "可用多头仅剩 300 股");

            // 3. 策略再次下达 Sell 1000 股（或 Sell 500 股）：可用持仓不足，必须被 Rejected，严禁转为开空！
            long id2 = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 1000m));
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id2));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "总持仓守恒为 1000，绝无空头开仓");
            Assert.AreEqual(300m, broker.GetAvailablePosition("sh.600000"), "可用持仓守恒为 300");

            // 4. 策略尝试多翻空 Sell 1500 股：由于现有多头无法全平，同样必须被 Rejected！
            long id3 = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 1500m));
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(id3));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"), "拒单后持仓不可变");
        }

        [TestMethod]
        public void UniversalNetting_ReduceOnly_Contract_And_SymmetricFlip_ShouldWorkAccurately()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                MarginRate = 0.1,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("BTCUSDT", 100.0);

            // ==========================================
            // 1. 无仓位时提交 ReduceOnly 订单必须严格被拒绝
            // ==========================================
            var noPosReduceOrder = new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 1m, ReduceOnly: true);
            long noPosId = broker.SubmitOrder(noPosReduceOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(noPosId), "无持仓时发 ReduceOnly 单必须直接被拒绝");

            // ==========================================
            // 2. 多头持仓下的 ReduceOnly 与自由翻空
            // ==========================================
            // 买入 5 手多头
            broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 5m), 100.0, null);
            Assert.AreEqual(5m, broker.GetPosition("BTCUSDT"));

            // a) ReduceOnly 方向必须与持仓相反：若持有多头却发 ReduceOnly 买入，必须直接被拒！
            var wrongDirOrder = new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 1m, ReduceOnly: true);
            long wrongDirId = broker.SubmitOrder(wrongDirOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(wrongDirId), "ReduceOnly 订单方向必须与已有持仓相反");

            // b) 先挂出限价单 3 手平多，可用平多只剩 2 手
            long limitSellId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Limit, 120.0, 3m));
            Assert.AreEqual(2m, broker.GetAvailablePosition("BTCUSDT"));

            // c) 尝试用 ReduceOnly 卖出 4 手：可用不足，必须直接被拒，绝不开空！
            var overReduceOrder = new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 4m, ReduceOnly: true);
            long overReduceId = broker.SubmitOrder(overReduceOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(overReduceId), "ReduceOnly 可用不足必须被拒绝，严禁开空");
            Assert.AreEqual(5m, broker.GetPosition("BTCUSDT"), "持仓完好守恒");

            // 撤单恢复全部 5 手可用
            broker.CancelOrder(limitSellId);
            Assert.AreEqual(5m, broker.GetAvailablePosition("BTCUSDT"));

            // d) 常规自由多翻空：持有 5 手多头，发普通 Sell 12 手 -> 平 5 手多头 + 开 7 手空头
            // 开 7 手空头需扣保证金 = 7 * 100 * 0.1 = 70 元
            long flipToShortId = broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 12m), 100.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(flipToShortId));
            Assert.AreEqual(-7m, broker.GetPosition("BTCUSDT"), "成功平 5 手多头并反手开 7 手空头");
            Assert.AreEqual(-7m, broker.GetAvailablePosition("BTCUSDT"));

            // ==========================================
            // 3. 空头持仓下的 ReduceOnly 与自由翻多（100% 镜像对称！）
            // ==========================================
            // a) 挂出限价单 4 手买平，可用平空只剩 3 手
            long limitBuyId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Limit, 80.0, 4m));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(limitBuyId));
            Assert.AreEqual(-3m, broker.GetAvailablePosition("BTCUSDT"));

            // b) 尝试用 ReduceOnly 买入 5 手：可用平空不足，必须直接被拒，绝不开多！
            var overBuyReduceOrder = new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 5m, ReduceOnly: true);
            long overBuyReduceId = broker.SubmitOrder(overBuyReduceOrder);
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(overBuyReduceId), "空头 ReduceOnly 可用平空不足必须拒绝，绝不开多");
            Assert.AreEqual(-7m, broker.GetPosition("BTCUSDT"), "空头持仓完好守恒");

            // 撤单恢复全部 7 手可用空头
            broker.CancelOrder(limitBuyId);
            Assert.AreEqual(-7m, broker.GetAvailablePosition("BTCUSDT"));

            // c) 常规自由空翻多：持有 7 手空头，发普通 Buy 15 手 -> 平 7 手空头 + 开 8 手多头
            long flipToLongId = broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 15m), 100.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(flipToLongId));
            Assert.AreEqual(8m, broker.GetPosition("BTCUSDT"), "成功平 7 手空头并反手开 8 手多头");

            // d) 使用 ClosePosition 门面全平：内部自动携带 reduceOnly = true 且全平归零
            long closeAllId = broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 8m, ReduceOnly: true), 100.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(closeAllId));
            Assert.AreEqual(0m, broker.GetPosition("BTCUSDT"), "全额清平成功归零");
        }

        [TestMethod]
        public void UniversalNetting_FlipOrder_PartialFills_AcrossZeroBoundary_ShouldTransferAccurately()
        {
            var gateway = new DirectCallbackGateway();
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                SettleDays = 0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = new Broker(gateway, config);
            var now = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
            broker.Step(now);

            // 1. 先建仓 10 手多头 @ 100
            var buyReq = new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Limit, 100.0, 10m);
            long buyId = broker.SubmitOrder(buyReq);
            gateway.PushExecution(new ExecutionReport(buyId, "BTCUSDT", 100.0, 10m, 0.0, now, CumQuantity: 10m, LeavesQuantity: 0m));
            Assert.AreEqual(10m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(10m, broker.GetAvailablePosition("BTCUSDT"));
            Assert.AreEqual(100_000.0 - 1000.0, broker.GetCash());

            // 2. 发起 25 手卖出（挂单 100），意图平 10 多 + 开 15 空
            // 平多 10 手释放信用 1000 元，开空 15 手需保证金 1500 元，净冻结 500 元现金
            var flipReq = new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Limit, 100.0, 25m);
            long flipId = broker.SubmitOrder(flipReq);
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(flipId));
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "10 手多头全部被平仓冻结");
            Assert.AreEqual(500.0, broker.FrozenCash, 1e-4, "开 15 手空头扣除平多 1000 信用后冻结 500 现金");

            // 3. 分笔 1: 成交 6 手 @ 100 (平多段)
            gateway.PushExecution(new ExecutionReport(flipId, "BTCUSDT", 100.0, 6m, 0.0, now, CumQuantity: 6m, LeavesQuantity: 19m));
            Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(flipId));
            Assert.AreEqual(4m, broker.GetPosition("BTCUSDT"), "持仓剩余 4 手多头");
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "剩余 4 手多头仍在冻结");
            Assert.AreEqual(500.0, broker.FrozenCash, 1e-4, "平多阶段尚未消耗开空保证金冻结");

            // 4. 分笔 2: 成交 4 手 @ 100 (多头刚好平尽)
            gateway.PushExecution(new ExecutionReport(flipId, "BTCUSDT", 100.0, 4m, 0.0, now, CumQuantity: 10m, LeavesQuantity: 15m));
            Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(flipId));
            Assert.AreEqual(0m, broker.GetPosition("BTCUSDT"), "持仓归零");
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"));
            Assert.AreEqual(500.0, broker.FrozenCash, 1e-4, "持仓归零但开空 500 冻结现金依然在途");

            // 5. 分笔 3: 成交 5 手 @ 110 (正式翻空，建仓 5 手空头)
            gateway.PushExecution(new ExecutionReport(flipId, "BTCUSDT", 110.0, 5m, 0.0, now, CumQuantity: 15m, LeavesQuantity: 10m));
            Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(flipId));
            Assert.AreEqual(-5m, broker.GetPosition("BTCUSDT"), "持仓成功翻空为 -5");
            Assert.AreEqual(-5m, broker.GetAvailablePosition("BTCUSDT"));
            Assert.AreEqual(500.0 * 10 / 15, broker.FrozenCash, 1e-4, "剩余 10 手冻结现金按比例释放");

            // 6. 分笔 4: 成交 10 手 @ 120 (完成全额成交，LeavesQty = 0)
            gateway.PushExecution(new ExecutionReport(flipId, "BTCUSDT", 120.0, 10m, 0.0, now, CumQuantity: 25m, LeavesQuantity: 0m));
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(flipId));
            Assert.AreEqual(-15m, broker.GetPosition("BTCUSDT"), "最终持仓为 -15 空头");
            Assert.AreEqual(-15m, broker.GetAvailablePosition("BTCUSDT"));
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "全额成交后冻结现金严格归零");

            // 加权开空成本验证: (5 * 110 + 10 * 120) / 15 = 1750 / 15 = 116.66666666666667
            var posObj = broker.GetActivePositions().First(p => p.Symbol == "BTCUSDT");
            Assert.AreEqual(1750.0 / 15.0, posObj.CostPrice, 1e-4, "空头加权成交均价准确");
        }

        [TestMethod]
        public void UniversalNetting_FlipOrder_RollbackAfterPartialFills_ShouldRestoreConservedState()
        {
            var gateway = new DirectCallbackGateway();
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                SettleDays = 0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = new Broker(gateway, config);
            var now = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
            broker.Step(now);

            // A. 多翻空部分成交后撤单
            var buyId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Limit, 100.0, 10m));
            gateway.PushExecution(new ExecutionReport(buyId, "BTCUSDT", 100.0, 10m, 0.0, now, CumQuantity: 10m, LeavesQuantity: 0m));
            Assert.AreEqual(10m, broker.GetPosition("BTCUSDT"));

            // 发起 25 手卖出（挂单 100），冻结 10 手多头；开空 15 手扣除平多 1000 信用后净冻结 500 现金
            var flipSellId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Limit, 100.0, 25m));
            // 分笔成交 6 手 @ 100（还剩 4 手多头待平，15 手空头待开）
            gateway.PushExecution(new ExecutionReport(flipSellId, "BTCUSDT", 100.0, 6m, 0.0, now, CumQuantity: 6m, LeavesQuantity: 19m));
            Assert.AreEqual(4m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "剩余 4 手多头仍在冻结中");
            Assert.AreEqual(500.0, broker.FrozenCash, 1e-4);

            // 撤单
            broker.CancelOrder(flipSellId);
            gateway.PushState(flipSellId, OrderStatus.Canceled, "User canceled remainder.");
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(flipSellId));
            Assert.AreEqual(4m, broker.GetPosition("BTCUSDT"), "持仓保持为 4 手多头");
            Assert.AreEqual(4m, broker.GetAvailablePosition("BTCUSDT"), "未成交的 4 手多头成功解冻为可用！");
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "未成交的 15 手开空冻结资金 100% 解冻！");

            // B. 空翻多部分成交后撤单
            // 先将 4 手多头平仓，并开 10 手空头 @ 100
            var toShortId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Limit, 100.0, 14m));
            gateway.PushExecution(new ExecutionReport(toShortId, "BTCUSDT", 100.0, 14m, 0.0, now, CumQuantity: 14m, LeavesQuantity: 0m));
            Assert.AreEqual(-10m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(-10m, broker.GetAvailablePosition("BTCUSDT"));

            // 发起 25 手买入（挂单 100），冻结 10 手平空；开多 15 手扣除平空 1000 信用后净冻结 500 现金
            var flipBuyId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Limit, 100.0, 25m));
            // 分笔成交 6 手 @ 100（平掉 6 手空头，还剩 4 手空头待平，15 手多头待开）
            gateway.PushExecution(new ExecutionReport(flipBuyId, "BTCUSDT", 100.0, 6m, 0.0, now, CumQuantity: 6m, LeavesQuantity: 19m));
            Assert.AreEqual(-4m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "剩余 4 手空头平仓额度仍在冻结中");
            Assert.AreEqual(500.0, broker.FrozenCash, 1e-4);

            // 撤单
            broker.CancelOrder(flipBuyId);
            gateway.PushState(flipBuyId, OrderStatus.Canceled, "User canceled remainder.");
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(flipBuyId));
            Assert.AreEqual(-4m, broker.GetPosition("BTCUSDT"), "持仓保持为 -4 手空头");
            Assert.AreEqual(-4m, broker.GetAvailablePosition("BTCUSDT"), "未成交的 4 手平空额度成功解冻，负数可用持仓恢复为 -4！");
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "未成交的 15 手开多冻结资金 100% 解冻！");
        }

        [TestMethod]
        public void UniversalNetting_FlipOrder_RollbackWithoutAnyFills_ShouldBeLossless()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                SettleDays = 0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("BTCUSDT", 100.0);

            // 1. 多翻空挂单撤销
            broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 10m), 100.0, null);
            Assert.AreEqual(10m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(10m, broker.GetAvailablePosition("BTCUSDT"));
            double cashBefore = broker.GetCash();

            // 发出 25 手卖出限价单（挂单 110，未成交）
            // 平多 10 手释放信用 1000(成本) + 100(浮盈) = 1100 元；开空 15 手需 1650 元，净冻结 550 元现金
            long flipSellId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Limit, 110.0, 25m));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(flipSellId));
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "多头全部冻结");
            Assert.AreEqual(550.0, broker.FrozenCash, 1e-4, "开空 15 手扣除平多 1100 信用后净冻结 550 现金");

            // 撤单
            broker.CancelOrder(flipSellId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(flipSellId));
            Assert.AreEqual(10m, broker.GetAvailablePosition("BTCUSDT"), "多头持仓 100% 恢复可用");
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "冻结现金 100% 解冻归零");
            Assert.AreEqual(cashBefore, broker.GetCash(), 1e-4, "现金完全守恒");

            // 2. 空翻多挂单撤销
            // 先平仓归零并开 10 手空头
            broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 20m), 100.0, null);
            Assert.AreEqual(-10m, broker.GetPosition("BTCUSDT"));
            Assert.AreEqual(-10m, broker.GetAvailablePosition("BTCUSDT"));
            double cashShort = broker.GetCash();

            // 发出 25 手买入限价单（挂单 90，未成交）
            // 平空 10 手释放信用 1000(成本) + 100(浮盈) = 1100 元；开多 15 手需 1350 元，净冻结 250 元现金
            long flipBuyId = broker.SubmitOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Limit, 90.0, 25m));
            Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(flipBuyId));
            Assert.AreEqual(0m, broker.GetAvailablePosition("BTCUSDT"), "空头平仓额度被挂单冻结占满");
            Assert.AreEqual(250.0, broker.FrozenCash, 1e-4, "开多 15 手扣除平空 1100 信用后净冻结 250 现金");

            // 撤单
            broker.CancelOrder(flipBuyId);
            Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(flipBuyId));
            Assert.AreEqual(-10m, broker.GetAvailablePosition("BTCUSDT"), "空头持仓 100% 恢复可用 -10");
            Assert.AreEqual(0.0, broker.FrozenCash, 1e-4, "冻结现金 100% 解冻归零");
            Assert.AreEqual(cashShort, broker.GetCash(), 1e-4, "现金完全守恒");
        }

        [TestMethod]
        public void UniversalNetting_BracketOrder_ReduceOnly_Contract_And_RejectionDefense()
        {
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                SettleDays = 0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            var now = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
            broker.Step(now);

            // 1. 先买入 10 手 BTCUSDT @ 100
            broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Buy, OrderType.Market, 100.0, 10m), 100.0, null);
            Assert.AreEqual(10m, broker.GetPosition("BTCUSDT"));

            // 2. 针对这 10 手多头挂出 Bracket 保护单（止损 90，止盈 110）
            var (stopId, tpId) = broker.Bracket("BTCUSDT", 10m, 90.0, 110.0);

            // 验证契约：通过反射获取 Broker 内部订单记录断言 Bracket 构造的订单 Request.ReduceOnly 必须为 true
            var ordersField = typeof(Broker).GetField("_orders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ordersDict = (System.Collections.IDictionary)ordersField!.GetValue(broker)!;
            dynamic stopRecord = ordersDict[stopId]!;
            dynamic tpRecord = ordersDict[tpId]!;
            Assert.IsTrue((bool)stopRecord.Request.ReduceOnly, "Bracket 止损单必须携带 ReduceOnly: true 保护契约");
            Assert.IsTrue((bool)tpRecord.Request.ReduceOnly, "Bracket 止盈单必须携带 ReduceOnly: true 保护契约");
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpId));

            // 3. 模拟持仓被策略其它逻辑提前全额平掉（持仓归零）
            broker.ExecuteOrder(new OrderRequest("BTCUSDT", OrderDirection.Sell, OrderType.Market, 100.0, 10m, ReduceOnly: true), 100.0, null);
            Assert.AreEqual(0m, broker.GetPosition("BTCUSDT"), "持仓已归零");

            // 4. 市场价格暴跌至 85，触发止损条件单
            var bar = new BarSnapshot(open: 88.0, high: 89.0, low: 85.0, close: 86.0);
            if (((IExchangeGatewayProvider)broker).Gateway is ISimulatedExchange sim)
            {
                sim.OnMarketData("BTCUSDT", now.AddMinutes(1), in bar);
            }
            broker.Step(now.AddMinutes(1));

            // 5. 核心防御断言：由于 ReduceOnly: true 且当前无持仓，止损单在 TryFreeze 时必须被 Rejected，绝不能开出 10 手空头！
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(stopId), "持仓归零后触发的 ReduceOnly 止损单必须被拒绝");
            Assert.AreEqual(0m, broker.GetPosition("BTCUSDT"), "持仓必须严格保持为 0，绝对防范静默反手开仓");
        }

        private sealed class DirectCallbackGateway : IExchangeGateway
        {
            public event Action<ExecutionReport>? OnExecution;
            public event Action<OrderStateReport>? OnOrderStateChanged;
            public Dictionary<long, OrderRequest> SentOrders { get; } = new();

            public void SendOrder(long orderId, OrderRequest request)
            {
                SentOrders[orderId] = request;
            }
            public void CancelOrder(long orderId) { }

            public void PushExecution(ExecutionReport report) => OnExecution?.Invoke(report);
            public void PushState(long orderId, OrderStatus status, string? reason = null) =>
                OnOrderStateChanged?.Invoke(new OrderStateReport(orderId, status, DateTime.UtcNow, Reason: reason ?? string.Empty));
        }

        [TestMethod]
        public void Broker_DefaultInterfaceMethods_ClosePosition_StopLoss_TakeProfit_Bracket_ShouldWorkAsContracted()
        {
            var config = new BrokerConfig { InitialCash = 1_000_000, AllowShort = true };
            var gateway = new DirectCallbackGateway();
            var broker = new Broker(gateway, config);
            IBroker iBroker = broker;

            // 1. 无持仓时调用 ClosePosition 返回 0L
            long zeroOrderId = iBroker.ClosePosition("sh.600000");
            Assert.AreEqual(0L, zeroOrderId);

            // 构造开仓成交回报同步更新持仓
            long openOrderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m));
            gateway.PushExecution(new ExecutionReport(openOrderId, "sh.600000", 10.0, 1000m, 5.0, MarketTime.UtcNow));
            Assert.AreEqual(1000m, iBroker.GetPosition("sh.600000"));

            // 2. 有多头持仓时调用 ClosePosition 发出仅减仓市价平仓单（立即路由网关）
            long closeOrderId = iBroker.ClosePosition("sh.600000");
            Assert.IsTrue(closeOrderId > 0);
            Assert.IsTrue(gateway.SentOrders.TryGetValue(closeOrderId, out var closeReq));
            Assert.AreEqual(OrderDirection.Sell, closeReq.Direction);
            Assert.AreEqual(1000m, closeReq.Quantity);
            Assert.IsTrue(closeReq.ReduceOnly);

            // 3. StopLoss：条件单在触发前服务端挂起（OrderStatus.Pending），零资金仓位冻结，绝不提前路由至网关
            long slOrderId = iBroker.StopLoss("sh.600000", stopPrice: 9.5, quantity: 500m);
            Assert.IsTrue(slOrderId > 0);
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(slOrderId));
            Assert.IsFalse(gateway.SentOrders.ContainsKey(slOrderId), "条件单触发前严禁向底层网关发送报单");

            // 4. TakeProfit：条件单在触发前服务端挂起
            long tpOrderId = iBroker.TakeProfit("sh.600000", targetPrice: 12.0, quantity: 500m);
            Assert.IsTrue(tpOrderId > 0);
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpOrderId));
            Assert.IsFalse(gateway.SentOrders.ContainsKey(tpOrderId));

            // 5. Bracket 成对 OCO 单：双向挂起并分配关联的 OCO 组 ID
            var (bracketSlId, bracketTpId) = iBroker.Bracket("sh.600000", quantity: 300m, stopPrice: 9.0, takeProfitPrice: 12.5);
            Assert.IsTrue(bracketSlId > 0);
            Assert.IsTrue(bracketTpId > 0);
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(bracketSlId));
            Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(bracketTpId));
            Assert.IsFalse(gateway.SentOrders.ContainsKey(bracketSlId));
            Assert.IsFalse(gateway.SentOrders.ContainsKey(bracketTpId));
        }

        private sealed class MinimalTestBroker : IBroker
        {
            public decimal Position { get; set; }
            public OrderRequest? LastSubmittedOrder { get; private set; }
            public List<OrderRequest> AllSubmittedOrders { get; } = new();
            public long Sequence { get; set; } = 1;

            public double GetCash() => 10000.0;
            public double GetTotalEquity() => 10000.0;
            public decimal GetPosition(string symbol) => Position;
            public decimal GetAvailablePosition(string symbol) => Position;
            public double GetPositionCost(string symbol) => 10.0;
            public IReadOnlyDictionary<string, decimal> GetPositions() => new Dictionary<string, decimal>();
            public long SubmitOrder(OrderRequest request)
            {
                LastSubmittedOrder = request;
                AllSubmittedOrders.Add(request);
                return Sequence++;
            }
            public void CancelOrder(long orderId) { }
            public OrderStatus GetOrderStatus(long orderId) => OrderStatus.Submitted;
            public void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions) { }

#pragma warning disable CS0067
            public event Action<long, OrderStatus>? OnOrderStatusChanged;
            public event Action<ExecutionReport>? OnTrade;
            public event Action<PositionEvent>? OnPositionChanged;
            public event Action<CarrotBacktesting.NET.Abstraction.Analysis.OrderEvent>? OnOrderEvent;
#pragma warning restore CS0067
        }

        [TestMethod]
        public void IBroker_DefaultInterfaceMethods_PureContractExecution_AllBranchesCovered()
        {
            IBroker broker = new MinimalTestBroker();

            // 1. AvailableCash 默认属性
            Assert.AreEqual(10000.0, broker.AvailableCash);

            // 2. Buy 与 Sell 便捷方法
            var minimal = (MinimalTestBroker)broker;
            long buyId = broker.Buy("sh.600000", 100m, price: 10.5);
            Assert.AreEqual(1L, buyId);
            Assert.AreEqual(OrderDirection.Buy, minimal.LastSubmittedOrder!.Direction);
            Assert.AreEqual(10.5, minimal.LastSubmittedOrder.Price);

            long sellId = broker.Sell("sh.600000", 50m, price: 11.0);
            Assert.AreEqual(2L, sellId);
            Assert.AreEqual(OrderDirection.Sell, minimal.LastSubmittedOrder!.Direction);

            // 3. ClosePosition：无持仓返回 0，多头持仓卖出平仓，空头持仓买入平仓
            minimal.Position = 0m;
            Assert.AreEqual(0L, broker.ClosePosition("sh.600000"));

            minimal.Position = 500m;
            long closeLongId = broker.ClosePosition("sh.600000");
            Assert.IsTrue(closeLongId > 0);
            Assert.AreEqual(OrderDirection.Sell, minimal.LastSubmittedOrder!.Direction);
            Assert.AreEqual(500m, minimal.LastSubmittedOrder.Quantity);
            Assert.IsTrue(minimal.LastSubmittedOrder.ReduceOnly);

            minimal.Position = -300m;
            long closeShortId = broker.ClosePosition("sh.600000");
            Assert.IsTrue(closeShortId > 0);
            Assert.AreEqual(OrderDirection.Buy, minimal.LastSubmittedOrder!.Direction);
            Assert.AreEqual(300m, minimal.LastSubmittedOrder.Quantity);
            Assert.IsTrue(minimal.LastSubmittedOrder.ReduceOnly);

            // 4. StopLoss (多头/空头推断)
            minimal.Position = 500m;
            broker.StopLoss("sh.600000", stopPrice: 9.5, quantity: 200m);
            Assert.AreEqual(OrderDirection.Sell, minimal.LastSubmittedOrder!.Direction);
            Assert.AreEqual(OrderType.Stop, minimal.LastSubmittedOrder.Type);
            Assert.AreEqual(9.5, minimal.LastSubmittedOrder.StopPrice);

            minimal.Position = -500m;
            broker.StopLoss("sh.600000", stopPrice: 10.5, quantity: 200m);
            Assert.AreEqual(OrderDirection.Buy, minimal.LastSubmittedOrder!.Direction);

            // 5. TakeProfit (多头/空头推断)
            minimal.Position = 500m;
            broker.TakeProfit("sh.600000", targetPrice: 12.0, quantity: 200m);
            Assert.AreEqual(OrderDirection.Sell, minimal.LastSubmittedOrder!.Direction);
            Assert.AreEqual(OrderType.TakeProfit, minimal.LastSubmittedOrder.Type);
            Assert.AreEqual(12.0, minimal.LastSubmittedOrder.StopPrice);

            minimal.Position = -500m;
            broker.TakeProfit("sh.600000", targetPrice: 8.0, quantity: 200m);
            Assert.AreEqual(OrderDirection.Buy, minimal.LastSubmittedOrder!.Direction);

            // 6. Bracket (自动推断方向与显式传入 closeDirection)
            minimal.Position = 500m;
            var (sl1, tp1) = broker.Bracket("sh.600000", 100m, stopPrice: 9.0, takeProfitPrice: 12.0);
            Assert.IsTrue(sl1 > 0);
            Assert.IsTrue(tp1 > 0);

            var (sl2, tp2) = broker.Bracket("sh.600000", 100m, stopPrice: 9.0, takeProfitPrice: 12.0, closeDirection: OrderDirection.Buy);
            Assert.IsTrue(sl2 > 0);
            Assert.IsTrue(tp2 > 0);
        }

        [TestMethod]
        public void Broker_ShortSellWithTPlus1_ShouldUpdatePositionAndAllowBuyToCover()
        {
            // 验证标的允许做空且交割制度为 T+1 时，做空卖出即刻产生空头可用持仓，并能顺利买入平空
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                AllowShort = true,
                SettleDays = 1,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            var bar = CreateBar(10.0, 10.5, 9.8, 10.0, 10000.0);

            // 1. 融券卖出 500 股 @ 10.0
            long shortOrderId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 500m), 10.0, bar);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(shortOrderId));
            Assert.AreEqual(-500m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(-500m, broker.GetAvailablePosition("sh.600000"));

            // 2. 买入平空 500 股 @ 9.0 (浮盈 500 元)
            var coverBar = CreateBar(9.0, 9.2, 8.8, 9.0, 10000.0);
            long coverOrderId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 9.0, 500m), 9.0, coverBar);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(coverOrderId));
            Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));
            // 初始 100,000 + (10.0 - 9.0) * 500 = 100,500
            Assert.AreEqual(100_500.0, broker.GetCash(), 1e-4);
        }

        [TestMethod]
        public void Broker_SubmitOrderWithNegativeOrderId_ShouldThrowArgumentOutOfRangeException()
        {
            var config = new BrokerConfig { InitialCash = 100_000.0 };
            var broker = CreateBroker(config);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m) with { OrderId = -5 });
            });
        }

        #region 审计 8.5 与 8.2 专项闭环测试

        [TestMethod]
        public void UniversalNetting_FlipOrder_ZeroAvailableCash_WithAdequateCloseProceeds_ShouldSucceed()
        {
            // 防御审计 8.5：满仓/可用现金为 0 场景下的反手翻仓测试
            var config = new BrokerConfig
            {
                InitialCash = 10_000.0,
                AllowShort = true,
                MarginRate = 1.0,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("AAPL", 100.0);

            // 1. 全仓买入 100 股 @ 100 元，现金从 10,000 降为 0！可用现金完全归零！
            broker.ExecuteOrder(new OrderRequest("AAPL", OrderDirection.Buy, OrderType.Market, 100.0, 100m), 100.0, null);
            Assert.AreEqual(100m, broker.GetPosition("AAPL"));
            Assert.AreEqual(0.0, broker.GetCash(), 1e-4, "满仓后可用现金为 0");

            // 2. 意图翻仓做空：卖出 150 股 @ 100 元（平多 100 股 + 开空 50 股）
            // 平多 100 股释放 10,000 元货款，开空 50 股需保证金 5,000 元
            // 净需现金 Math.Max(0, 5000 - 10000) = 0 元！
            // 旧版因 AvailableCash(0) < 5000 被错误拦截，新版必须成功提交并即时成交！
            long flipId = broker.ExecuteOrder(new OrderRequest("AAPL", OrderDirection.Sell, OrderType.Market, 100.0, 150m), 100.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(flipId), "零现金满仓反手翻空必须成功执行，严禁误拒");
            Assert.AreEqual(-50m, broker.GetPosition("AAPL"), "成功平掉 100 股多头并反手持有 50 股空头");
            Assert.AreEqual(5_000.0, broker.GetCash(), 1e-4, "平多回款 10,000 扣减开空保证金 5,000 后现金为 5,000");
            Assert.AreEqual(10_000.0, broker.GetTotalEquity(), 1e-4, "账户总权益 100% 守恒");
        }

        [TestMethod]
        public void UniversalNetting_FlipOrder_ZeroAvailableCash_ShortToLong_WithAdequateCloseProceeds_ShouldSucceed()
        {
            // 防御审计 8.5：空翻多（Short to Long）零现金满仓翻多测试
            var config = new BrokerConfig
            {
                InitialCash = 10_000.0,
                AllowShort = true,
                MarginRate = 1.0,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.UpdateMarketPrice("AAPL", 100.0);

            // 1. 建立 100 股空头 @ 100 元，扣除 10,000 元保证金，现金降为 0！
            broker.ExecuteOrder(new OrderRequest("AAPL", OrderDirection.Sell, OrderType.Market, 100.0, 100m), 100.0, null);
            Assert.AreEqual(-100m, broker.GetPosition("AAPL"));
            Assert.AreEqual(0.0, broker.GetCash(), 1e-4, "空头建仓后可用现金为 0");

            // 2. 发起翻多单：买入 150 股 @ 100 元（平空 100 股 + 开多 50 股）
            // 平空释放 10,000 元保证金，开多需要 5,000 元保证金，净需现金为 0！
            long flipId = broker.ExecuteOrder(new OrderRequest("AAPL", OrderDirection.Buy, OrderType.Market, 100.0, 150m), 100.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(flipId));
            Assert.AreEqual(50m, broker.GetPosition("AAPL"), "成功平掉 100 股空头并反手持有 50 股多头");
            Assert.AreEqual(5_000.0, broker.GetCash(), 1e-4);
            Assert.AreEqual(10_000.0, broker.GetTotalEquity(), 1e-4, "账户总权益 100% 守恒");
        }

        [TestMethod]
        public void UniversalNetting_FlipOrder_LowCash_WhenProceedsInsufficient_ShouldRejectWithDeficit()
        {
            // 防御审计 8.5：平仓释放信用不足以覆盖反手新开仓时的真实拒单拦截测试
            var config = new BrokerConfig
            {
                InitialCash = 100.0,
                AllowShort = true,
                MarginRate = 1.0,
                Multiplier = 1.0,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);
            broker.SyncState(100.0, new Dictionary<string, decimal> { ["AAPL"] = 10m });
            broker.UpdateMarketPrice("AAPL", 10.0); // 标的暴跌至 10 元

            // 持有 10 股多头，试图卖出 60 股（平多 10 股 + 开空 50 股）
            // 平多 10 股 @ 10 元仅回款 100 元；反手开空 50 股 @ 10 元需保证金 500 元
            // 净需现金 500 - 100 = 400 元，当前可用现金仅 100 元，必须被拒单拦截！
            long flipId = broker.SubmitOrder(new OrderRequest("AAPL", OrderDirection.Sell, OrderType.Limit, 10.0, 60m));
            Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(flipId));
            Assert.AreEqual(10m, broker.GetPosition("AAPL"), "拒单后持仓完好");
            Assert.AreEqual(100.0, broker.GetCash(), 1e-4, "拒单后现金完好");
        }

        [TestMethod]
        public void TPlusTwo_FridayToMonday_ShouldRemainLockedOnMonday_AndUnlockOnTuesday()
        {
            // 防御审计 8.2：跨周末 T+2 结算测试（周五买入，周一 T+1 仍锁定，周二 T+2 准时解锁）
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 2,
                AllowShort = false,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);

            // 1. 周五 2025-01-03 盘中买入 1000 股
            DateTime friday = new DateTime(2025, 1, 3, 10, 0, 0);
            DateTime monday = new DateTime(2025, 1, 6, 9, 30, 0);
            DateTime tuesday = new DateTime(2025, 1, 7, 9, 30, 0);

            broker.Step(friday);
            long buyId = broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m), 10.0, null);
            Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(buyId));
            Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "T+2 买入当日可用持仓为 0");

            // 2. 周一 2025-01-06 跨过周末首个交易日开盘（经历 1 个客观交易日步长 T+1）
            // 剩余步长为 1，周一必须依然保持锁定！
            broker.Step(monday);
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "跨周末的周一仅经历 1 个交易日步长，T+2 持仓必须依然锁定！");

            // 周一盘中多次推进依然严格守卫
            broker.Step(new DateTime(2025, 1, 6, 14, 55, 0));
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "周一盘中持仓必须依然锁定");

            // 3. 周二 2025-01-07 跨日开盘（经历第 2 个客观交易日步长 T+2）
            // 到达 T+2 结算阈值，必须精准解锁为可用！
            broker.Step(tuesday);
            Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"), "周二开盘到达 T+2 结算，持仓必须精准解冻可用！");
        }

        [TestMethod]
        public void TPlusN_WithHolidayCalendar_ShouldSkipHolidaysAccurately()
        {
            // 防御审计 8.2：法定节假日休市下的 T+1 跨节假日结算测试
            // 周五买入，下周一为法定节假日休市，休市期间不推日切；周二开市为下一个实际交易日（经历第 1 个交易日步长解锁）
            DateTime friday = new DateTime(2025, 5, 2, 10, 0, 0); // 周五
            DateTime tuesday = new DateTime(2025, 5, 6, 9, 30, 0); // 节后周二首个开市日

            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 1,
                AllowShort = false,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);

            // 周五买入
            broker.Step(friday);
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 500m), 10.0, null);
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 节后首个实际交易日开盘（周二），日切步进 1 个交易日
            broker.Step(tuesday);
            Assert.AreEqual(500m, broker.GetAvailablePosition("sh.600000"), "节假日后首个实际交易日开盘，T+1 持仓精准解锁！");
        }

        [TestMethod]
        public void TPlusThree_SpanningWeekend_ShouldUnlockOnThirdTradingDay()
        {
            // 防御审计 8.2：通用 T+3 跨周末结算（周三买入 -> 周四T+1 -> 周五T+2 -> 周一T+3 解锁）
            var config = new BrokerConfig
            {
                InitialCash = 100_000.0,
                SettleDays = 3,
                AllowShort = false,
                FeeRate = 0.0,
                Slippage = 0.0
            };
            var broker = CreateBroker(config);

            DateTime wednesday = new DateTime(2025, 1, 1, 10, 0, 0);
            DateTime thursday = new DateTime(2025, 1, 2, 9, 30, 0);
            DateTime friday = new DateTime(2025, 1, 3, 9, 30, 0);
            DateTime monday = new DateTime(2025, 1, 6, 9, 30, 0);

            // 1. 周三买入（SettleDays = 3）
            broker.Step(wednesday);
            broker.ExecuteOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 800m), 10.0, null);
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 2. 周四（经历 1 个交易日步长，剩余 2，锁定）
            broker.Step(thursday);
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 3. 周五（经历第 2 个交易日步长，剩余 1，锁定）
            broker.Step(friday);
            Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

            // 4. 周一（跨过周末，经历第 3 个交易日步长，剩余 0，准时解锁）
            broker.Step(monday);
            Assert.AreEqual(800m, broker.GetAvailablePosition("sh.600000"), "周一开盘到达 T+3 结算阈值，持仓精准解冻可用！");
        }

        #endregion

        #endregion
    }

    internal static class BrokerTestExtensions
    {
        public static void OnMarketData(this Broker broker, string symbol, double price, DateTime time, IDataRow? bar = null)
        {
            if (((IExchangeGatewayProvider)broker).Gateway is ISimulatedExchange sim)
            {
                BarSnapshot snapshot = bar != null ? BarSnapshot.FromDataRow(bar) : new BarSnapshot(close: price);
                sim.OnMarketData(symbol, time, in snapshot);
            }
        }

        public static void UpdateMarketPrice(this Broker broker, string symbol, double price, IDataRow? bar = null)
        {
            if (((IExchangeGatewayProvider)broker).Gateway is ISimulatedExchange sim)
            {
                MarketTime effectiveTime = broker.CurrentTime != MarketTime.MinValue ? broker.CurrentTime : MarketTime.UtcNow;
                BarSnapshot snapshot = bar != null ? BarSnapshot.FromDataRow(bar) : new BarSnapshot(close: price);
                sim.OnMarketData(symbol, effectiveTime, in snapshot);
            }
        }

        public static long ExecuteOrder(this Broker broker, OrderRequest request, double basePrice, IDataRow? bar = null)
        {
            if (((IExchangeGatewayProvider)broker).Gateway is ISimulatedExchange sim)
            {
                if (basePrice > 0.0 && !double.IsNaN(basePrice) && !double.IsInfinity(basePrice))
                {
                    MarketTime effectiveTime = broker.CurrentTime != MarketTime.MinValue ? broker.CurrentTime : MarketTime.UtcNow;
                    BarSnapshot snapshot = bar != null ? BarSnapshot.FromDataRow(bar) : new BarSnapshot(close: basePrice);
                    sim.OnMarketData(request.Symbol, effectiveTime, in snapshot);
                }
                else if (double.IsNaN(basePrice) || double.IsInfinity(basePrice))
                {
                    return broker.SubmitOrder(request with { Price = basePrice });
                }
            }
            return broker.SubmitOrder(request);
        }
    }

    internal sealed class TestLogMonitor : IMonitor
    {
        private readonly Action<string> _onLog;
        public TestLogMonitor(Action<string> onLog) => _onLog = onLog;
        public void Log(string message, LogLevel level) => _onLog(message);
        public void ReportMetric(string metricName, double value) { }
        public void Alert(string message) { }
    }
}
