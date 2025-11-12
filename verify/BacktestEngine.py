import json
import os
from pathlib import Path

from tqdm import tqdm

import env
from StrategyContext import *


def run_backtest(data_map: dict[str, pd.DataFrame], strategy) -> list[dict]:
    """
    根据策略类型，选择并运行回测模式。
    """
    print("\n--- Starting Backtest ---")
    
    # 通过检查方法是否存在，来判断策略类型
    if hasattr(strategy, 'check_entry') and hasattr(strategy, 'check_exit'):
        print(f"Running in [Trade Simulation Mode] for strategy: {strategy.__class__.__name__}")
        return _run_trade_simulation(data_map, strategy)
    elif hasattr(strategy, 'check_signal'):
        print(f"Running in [Signal Generation Mode] for strategy: {strategy.__class__.__name__}")
        return _run_signal_generation(data_map, strategy)
    else:
        raise TypeError("Provided strategy does not conform to a known strategy interface.")

def _run_signal_generation(data_map: dict[str, pd.DataFrame], strategy) -> list[dict]:
    """
    信号生成模式：记录所有满足条件的买点。
    """
    all_signals = []
    
    for symbol, stock_df in tqdm(data_map.items(), desc="Signal Generation"):
        if stock_df.empty:
            continue
        
        context = StrategyContext(stock_df)
        last_signal_state = False
        
        for i in range(len(stock_df)):
            context.current_index = i
            entry_reason = strategy.check_signal(context)
            
            current_signal_state = (entry_reason is not None)
            if current_signal_state and not last_signal_state:
                price = context.get_close(0)
                if price > 0:
                    # 将信号记录为一个【未平仓】的交易字典
                    all_signals.append({
                        "StockCode": symbol,
                        "EntryReason": entry_reason,
                        "EntryDate": stock_df.at[i, 'date'].strftime('%Y-%m-%dT%H:%M:%S'),
                        "EntryPrice": price,
                        "ExitReason": None, "ExitDate": None, "ExitPrice": None,
                        "IsClosed": False, "HoldingPeriod": 0, "HighestPriceSinceEntry": price
                    })
            last_signal_state = current_signal_state
            
    return all_signals

def _run_trade_simulation(data_map: dict[str, pd.DataFrame], strategy) -> list[dict]:
    """
    交易模拟模式：一次只持有一笔交易。
    """
    all_trades = []

    for symbol, stock_df in tqdm(data_map.items(), desc="Trade Simulation"):
        if stock_df.empty:
            continue

        current_trade = None
        context = StrategyContext(stock_df)

        for i in range(len(stock_df)):
            context.current_index = i
            price = context.get_close(0)
            if price is None or price <= 0:
                continue

            if current_trade is None:
                # 空仓状态
                entry_reason = strategy.check_entry(context)
                if entry_reason:
                    current_trade = {
                        "StockCode": symbol, "EntryReason": entry_reason,
                        "EntryDate": stock_df.at[i, 'date'], "EntryPrice": price,
                        "ExitReason": None, "ExitDate": None, "ExitPrice": None,
                        "IsClosed": False, "HoldingPeriod": 0, "HighestPriceSinceEntry": price
                    }
            else:
                # 持仓状态
                current_trade["HoldingPeriod"] += 1
                current_trade["HighestPriceSinceEntry"] = max(current_trade["HighestPriceSinceEntry"], price)
                
                exit_reason = strategy.check_exit(context, current_trade)
                if exit_reason:
                    current_trade["ExitReason"] = exit_reason
                    current_trade["ExitDate"] = stock_df.at[i, 'date']
                    current_trade["ExitPrice"] = price
                    current_trade["IsClosed"] = True
                    all_trades.append(current_trade)
                    current_trade = None
        
        # 处理期末未平仓的交易
        if current_trade:
            all_trades.append(current_trade)

    # 格式化日期
    for trade in all_trades:
        if isinstance(trade['EntryDate'], pd.Timestamp):
            trade['EntryDate'] = trade['EntryDate'].strftime('%Y-%m-%dT%H:%M:%S')
        if isinstance(trade['ExitDate'], pd.Timestamp):
            trade['ExitDate'] = trade['ExitDate'].strftime('%Y-%m-%dT%H:%M:%S')

    return all_trades

def save_backtest_summary(trades: list[dict], out: str = "signal_summary.json"):
    """
    生成回测摘要并保存为JSON。
    """
    closed_trades = [t for t in trades if t['IsClosed']]
    open_trades = [t for t in trades if not t['IsClosed']]

    summary_data = {
        "stats": {
            "total_trades": len(trades),
            "closed_trades_count": len(closed_trades),
            "open_trades_count": len(open_trades)
        },
        "trades": sorted(trades, key=lambda x: x['EntryDate']) # 保存排序后的交易列表
    }

    try:
        os.makedirs(env.OUTPUT_DIR, exist_ok=True)
        output_filepath = Path(env.OUTPUT_DIR) / out
        with open(output_filepath, 'w', encoding='utf-8') as f:
            json.dump(summary_data, f, ensure_ascii=False, indent=4, default=str)
        print(f"\n回测结果已成功写入到: {output_filepath}")
        
        print("\n--- Backtest Summary ---")
        print(f"Total Trades: {len(trades)}")
        print(f"  - Closed: {len(closed_trades)}")
        print(f"  - Open: {len(open_trades)}")
        print("------------------------")

    except Exception as e:
        print(f"\n错误: 写入 JSON 文件失败: {e}")