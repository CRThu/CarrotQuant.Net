from StrategyContext import *


class VolumeSignalStrategy:
    """
    【信号生成模式】策略：当日成交量 > 前5日平均成交量的2.5倍
    """
    def check_signal(self, context: StrategyContext) -> str | None:
        current_volume = context.get_volume(0)
        if current_volume is None or current_volume == 0:
            return None

        past_volumes = []
        for i in range(1, 6):
            past_vol = context.get_volume(-i)
            if past_vol is None:
                return None # 历史数据不足
            past_volumes.append(past_vol)
        
        if current_volume > (sum(past_volumes) / len(past_volumes)) * 2.5:
            return "VolumeSpike"
        return None


class PriceStrategy:
    """
    【交易模拟模式】策略：收盘价比昨天高则买入，持5天卖出
    """
    def check_entry(self, context: StrategyContext) -> str | None:
        close0 = context.get_close(0)
        close1 = context.get_close(-1)
        if close0 is not None and close1 is not None and close0 > close1:
            return "PriceUp"
        return None

    def check_exit(self, context: StrategyContext, trade: dict) -> str | None:
        if trade['HoldingPeriod'] >= 5:
            return "FixedPeriodExit"
        return None