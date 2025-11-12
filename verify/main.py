import env
from DataFeed import *
from BacktestEngine import *
from ExampleStrategy import *

def main():
    data_map = load_data(env.DATA_ROOT_PATH)
    if not data_map:
        return
    print(f"\n数据加载完成，共 {len(data_map)} 只股票。")
    save_data_summary(data_map)

    # 2. 运行回测 (模式1：信号生成)
    signal_strategy = VolumeSignalStrategy()
    signal_trades = run_backtest(data_map, signal_strategy)
    save_backtest_summary(signal_trades, "signal_mode_summary.json")

    # 3. 运行回测 (模式2：交易模拟)
    trade_strategy = PriceStrategy()
    trade_trades = run_backtest(data_map, trade_strategy)
    save_backtest_summary(trade_trades, "trade_mode_summary.json")

if __name__ == "__main__":
    main()