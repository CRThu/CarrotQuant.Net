import pandas as pd

class StrategyContext:
    def __init__(self, stock_df: pd.DataFrame):
        self.series = stock_df
        self.current_index = 0

    def get_value(self, column: str, offset: int):
        """ 获取相对于当前点的数据 """
        target_index = self.current_index + offset
        if 0 <= target_index < len(self.series):
            return self.series.at[target_index, column]
        return None

    def get_open(self, offset: int):
        return self.get_value('open', offset)

    def get_close(self, offset: int):
        return self.get_value('close', offset)

    def get_volume(self, offset: int):
        return self.get_value('volume', offset)
