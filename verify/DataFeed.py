import json
import os
import pandas as pd
from pathlib import Path
from tqdm import tqdm
import random

import env

def load_data(root_path: str) -> dict[str, pd.DataFrame]:
    """
    扫描、读取所有CSV文件，并返回一个以股票代码为键、DataFrame为值的字典。
    """
    root_path_obj = Path(root_path)
    all_csv_files = list(root_path_obj.rglob("*.csv"))
    
    if not all_csv_files:
        print(f"错误: 在 '{root_path}' 目录下没有找到任何 .csv 文件。")
        return {}

    print(f"找到 {len(all_csv_files)} 个CSV文件，开始加载...")

    data_map = {}

    for file_path in tqdm(all_csv_files, desc="Processing files"):
        try:
            # 读取CSV，只选择我们需要的列
            df = pd.read_csv(file_path, usecols=list(env.COLUMNS_TO_USE.keys()))
            
            # 重命名列
            df = df.rename(columns=env.COLUMNS_TO_USE)
            
            # --- 数据清洗 (现在在单个DataFrame上进行) ---
            numeric_cols = ['open', 'high', 'low', 'close', 'volume']
            for col in numeric_cols:
                df[col] = pd.to_numeric(df[col], errors='coerce')
            
            df[numeric_cols] = df[numeric_cols].fillna(0)
            df['date'] = pd.to_datetime(df['date'])
            
            # 按日期排序
            df = df.sort_values(by='date').reset_index(drop=True)

            # 将处理好的 DataFrame 存入字典
            symbol = file_path.stem
            data_map[symbol] = df

        except Exception as e:
            print(f"\n处理文件 {file_path.name} 时出错: {e}")
            continue
            
    return data_map

def save_data_summary(data_map: dict[str, pd.DataFrame], out: str = "data_summary.json"):
    """
    计算摘要信息并保存到JSON文件。
    """
    if not data_map:
        print("数据为空，无法生成摘要。")
        return

    stock_count = len(data_map)
    ticks_count = sum(len(df) for df in data_map.values())
    all_dates = pd.concat([df['date'] for df in data_map.values() if not df.empty])
    start_date = all_dates.min().strftime('%Y-%m-%d') if not all_dates.empty else None
    end_date = all_dates.max().strftime('%Y-%m-%d') if not all_dates.empty else None

    sample_point = None
    random_symbol = random.choice(list(data_map.keys()))
    random_df = data_map[random_symbol]
    if not random_df.empty:
        random_index = random.randint(0, len(random_df) - 1)
        sample_point = random_df.iloc[random_index].to_dict()
        sample_point['date'] = sample_point['date'].strftime('%Y-%m-%d')
        sample_point['symbol'] = random_symbol


    summary_data = {
        "stats": {
            "stock_count": stock_count,
            "ticks_count": ticks_count,
            "date":
            {
                "start": start_date,
                "end": end_date
            }
        },
        "sample": sample_point
    }

    try:
        # a. 确保输出目录存在
        os.makedirs(env.OUTPUT_DIR, exist_ok=True)

        # b. 构造完整的文件路径
        output_filepath = os.path.join(env.OUTPUT_DIR, out)
        with open(output_filepath, 'w', encoding='utf-8') as f:
            json.dump(summary_data, f, ensure_ascii=False, indent=4, default=str)
        print(f"\n数据摘要信息已成功写入到: {output_filepath}")
        
        print("\n--- JSON Summary ---")
        print(json.dumps(summary_data, indent=4, default=str))
        print("--------------------")
    except Exception as e:
        print(f"\n错误: 写入 JSON 文件失败: {e}")

def main():
    """
    主函数
    """
    data_map = load_data(env.DATA_ROOT_PATH)

    if not data_map:
        print("未能成功加载任何数据。")
        return

    print("\n数据加载完成。")
    print(f"总共加载了 {len(data_map)} 只股票的数据。")

    save_data_summary(data_map)
    
if __name__ == "__main__":
    main()