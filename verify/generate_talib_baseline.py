"""
TA-Lib 权威基准数据生成器 (TA-Lib Benchmark Generator)
生成两套高精度无随机数全周期对账数据集：
1. talib_baseline_market.json: 基于真实 A 股历史行情 (sh.600000 浦发银行 2021~2022 两年连续 485 根日 K 线)
   - 覆盖超短 (SMA5, EMA5)、短 (SMA20, EMA10, EMA20)、中 (SMA60, EMA60)、长 (SMA120, EMA120)、超长/年线 (SMA250, EMA250)
   - 覆盖 RSI6 (超短敏感型) 与 RSI14 (经典型)
   - 覆盖 BOLL(20, 2.0)、MACD(12, 26, 9)、ATR(14)
2. talib_baseline_synthetic.json: 基于 300 根确定性数学公理序列 (常数、阶跃、持续暴涨、持续暴跌)
   - 检验长短均线与振荡指标在极端边界下的数值稳定性

格式规范：标准 JSON 数组，每个 Bar 独占一行（One-Bar-Per-Line）。
"""

import json
from pathlib import Path
import numpy as np
import pandas as pd
import talib


def compute_kdj(high, low, close, n=9, m1=3, m2=3):
    """
    计算中国 A 股通达信标准 KDJ (9, 3, 3)。
    """
    k_alpha = 1.0 / m1
    d_alpha = 1.0 / m2
    k_list, d_list, j_list = [], [], []
    k, d = 50.0, 50.0
    for i in range(len(close)):
        start_i = max(0, i - n + 1)
        hn = np.max(high[start_i:i+1])
        ln = np.min(low[start_i:i+1])
        rng = hn - ln
        rsv = 50.0 if rng <= 1e-12 else ((close[i] - ln) / rng) * 100.0
        k = (1.0 - k_alpha) * k + k_alpha * rsv
        d = (1.0 - d_alpha) * d + d_alpha * k
        j = 3.0 * k - 2.0 * d
        if i < n - 1:
            k_list.append(np.nan)
            d_list.append(np.nan)
            j_list.append(np.nan)
        else:
            k_list.append(k)
            d_list.append(d)
            j_list.append(j)
    return np.array(k_list), np.array(d_list), np.array(j_list)


def compute_indicators(open_p, high, low, close, volume, include_year_line=True):
    """
    调用 TA-Lib C 内核计算标准参考指标全家桶。
    """
    # 1. 均线谱系 (SMA: 5, 20, 60, 120, 250)
    sma5 = talib.SMA(close, 5)
    sma20 = talib.SMA(close, 20)
    sma60 = talib.SMA(close, 60)
    sma120 = talib.SMA(close, 120)
    sma250 = talib.SMA(close, 250) if include_year_line and len(close) >= 250 else np.full_like(close, np.nan)

    # 2. 指数平滑均线谱系 (EMA: 5, 10, 20, 60, 120, 250)
    ema5 = talib.EMA(close, 5)
    ema10 = talib.EMA(close, 10)
    ema20 = talib.EMA(close, 20)
    ema60 = talib.EMA(close, 60)
    ema120 = talib.EMA(close, 120)
    ema250 = talib.EMA(close, 250) if include_year_line and len(close) >= 250 else np.full_like(close, np.nan)

    # 3. 加权移动平均线谱系 (WMA: 5, 20, 60, 120)
    wma5 = talib.WMA(close, 5)
    wma20 = talib.WMA(close, 20)
    wma60 = talib.WMA(close, 60)
    wma120 = talib.WMA(close, 120)

    # 4. 布林带 (20, 2.0)
    up, mid, low_b = talib.BBANDS(close, 20, 2.0, 2.0, matype=0)

    # 5. MACD (12, 26, 9) - 连续流式 EMA 原语组合
    ema12 = talib.EMA(close, 12)
    ema26 = talib.EMA(close, 26)
    dif = ema12 - ema26
    valid_mask = ~np.isnan(dif)
    dea = np.full_like(close, np.nan)
    if np.any(valid_mask):
        dea[valid_mask] = talib.EMA(dif[valid_mask], 9)
    hist = (dif - dea) * 2.0

    # 6. RSI 谱系 (RSI6: A股短线敏感型, RSI14: 经典型)
    rsi6 = talib.RSI(close, 6)
    rsi14 = talib.RSI(close, 14)

    # 7. ATR 真实波幅 (14)
    atr14 = talib.ATR(high, low, close, 14)

    # 8. SAR 抛物转向系统 (0.02, 0.2)
    sar = talib.SAR(high, low, acceleration=0.02, maximum=0.2)

    # 9. VWAP (全周期累计与滚动 20)
    tp = (high + low + close) * (1.0 / 3.0)
    cum_pv = np.cumsum(tp * volume)
    cum_v = np.cumsum(volume)
    vwap = np.where(cum_v > 0, cum_pv / cum_v, tp)
    pv_s = pd.Series(tp * volume)
    v_s = pd.Series(volume)
    vwap20 = (pv_s.rolling(20).sum() / v_s.rolling(20).sum()).to_numpy()

    # 10. KDJ (9, 3, 3)
    k_vals, d_vals, j_vals = compute_kdj(high, low, close, 9, 3, 3)

    df = pd.DataFrame({
        "open": np.round(open_p, 4),
        "high": np.round(high, 4),
        "low": np.round(low, 4),
        "close": np.round(close, 4),
        "volume": np.round(volume, 2),
        "sma5": sma5,
        "sma20": sma20,
        "sma60": sma60,
        "sma120": sma120,
        "sma250": sma250,
        "ema5": ema5,
        "ema10": ema10,
        "ema20": ema20,
        "ema60": ema60,
        "ema120": ema120,
        "ema250": ema250,
        "wma5": wma5,
        "wma20": wma20,
        "wma60": wma60,
        "wma120": wma120,
        "boll_up": up,
        "boll_mid": mid,
        "boll_low": low_b,
        "macd_dif": dif,
        "macd_dea": dea,
        "macd_hist": hist,
        "rsi6": rsi6,
        "rsi14": rsi14,
        "atr14": atr14,
        "sar": sar,
        "vwap": vwap,
        "vwap20": vwap20,
        "kdj_k": k_vals,
        "kdj_d": d_vals,
        "kdj_j": j_vals
    })
    return df.replace({np.nan: None}).to_dict(orient="records")


def save_as_one_bar_per_line(records, file_path):
    with open(file_path, "w", encoding="utf-8") as f:
        f.write("[\n")
        total = len(records)
        for i, r in enumerate(records):
            line = json.dumps(r, separators=(",", ":"))
            if i < total - 1:
                f.write(line + ",\n")
            else:
                f.write(line + "\n")
        f.write("]\n")


def generate():
    root = Path(__file__).resolve().parent.parent
    out_dir = root / "CarrotBackTesting.NET.TestData" / "Indicators"
    out_dir.mkdir(parents=True, exist_ok=True)

    # 1. 真实 A 股数据源基准 (合并 2021 与 2022 年数据，共 485 根连续日 K，完整覆盖 250 日年线)
    csv_root = root / "CarrotBackTesting.NET.TestData" / "test_data_root" / "csv" / "ashare.kline.1d.raw.baostock"
    csv_2021 = csv_root / "year=2021" / "sh.600000.csv"
    csv_2022 = csv_root / "year=2022" / "sh.600000.csv"

    if not csv_2021.exists() or not csv_2022.exists():
        raise FileNotFoundError("未找到 2021 或 2022 年行情测试文件！")

    df_2021 = pd.read_csv(csv_2021)
    df_2022 = pd.read_csv(csv_2022)
    m_df = pd.concat([df_2021, df_2022], ignore_index=True)

    market_records = compute_indicators(
        m_df["open"].values,
        m_df["high"].values,
        m_df["low"].values,
        m_df["close"].values,
        m_df["volume"].values,
        include_year_line=True
    )

    market_json_path = out_dir / "talib_baseline_market.json"
    save_as_one_bar_per_line(market_records, market_json_path)
    print(f"✅ [1/2] 真实市场基准已就绪: {market_json_path} (覆盖长短均线至 250 年线, 共 {len(market_records)} bars)")

    # 2. 纯确定性数学物理公理序列 (扩展至 300 步，支持中长周期 60、120 阶跃检验)
    p_constant = [100.0] * 80                        # 80 步常数序列 (足够覆盖 SMA60/EMA60 初始化)
    p_step = [150.0] * 80                            # 80 步阶跃序列 (检验中长周期的阶跃过渡)
    p_ramp_up = [150.0 + i * 2.0 for i in range(70)]  # 70 步单边上涨 (检验 RSI6/RSI14 触顶)
    p_ramp_down = [290.0 - i * 3.0 for i in range(70)]# 70 步单边暴跌 (检验 RSI6/RSI14 触底与熊市负数 DIF)

    close_syn = np.array(p_constant + p_step + p_ramp_up + p_ramp_down, dtype=np.float64)
    high_syn = close_syn * 1.01
    low_syn = close_syn * 0.99
    open_syn = close_syn
    volume_syn = np.full_like(close_syn, 100000.0)

    synthetic_records = compute_indicators(
        open_syn, high_syn, low_syn, close_syn, volume_syn,
        include_year_line=False  # 合成数据 300 步测试至 120 周期
    )

    synthetic_json_path = out_dir / "talib_baseline_synthetic.json"
    save_as_one_bar_per_line(synthetic_records, synthetic_json_path)
    print(f"✅ [2/2] 数学公理基准已就绪: {synthetic_json_path} (共 {len(synthetic_records)} bars)")


if __name__ == "__main__":
    generate()
