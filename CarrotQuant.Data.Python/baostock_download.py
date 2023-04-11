import pandas as pd
import concurrent.futures
import random
import os
import time


# 探测是否存放路径为空, 若为空则新建
def dir_detect(save_dir: str):
    if not os.path.exists(save_dir):
        os.makedirs(save_dir)


# define a function to write data to dataframe
def download_and_store_klines_callfunc(thread_id, save_dir: str,
                                       stock_name: str, start_time: str, end_time: str,
                                       frequency: str, adjust: str):
    # 处理参数
    # TODO

    df = download_klines(stock_name)

    # 处理数据
    # TODO

    store_klines(save_dir, str(thread_id), df)
    return len(df.index), stock_name


def download_klines(stock_name):
    df = pd.DataFrame({'data': [stock_name]})
    return df


def store_klines(save_dir: str, stock_code: str, df: pd.DataFrame):
    df.to_csv(os.path.join(save_dir, f'{stock_code}.csv'), mode='w', index=False)


def baostock_klines_download(stock_list: str, save_dir: str,
                             start_time='1990-01-01', end_time=None,
                             frequency='day', adjust='backward',
                             max_workers=128):
    dir_detect('datadir')

    with concurrent.futures.ThreadPoolExecutor(max_workers=max_workers) as executor:
        # submit the tasks to the executor
        futures = [
            executor.submit(download_and_store_klines_callfunc, thread_id
                            , save_dir, stock_list[thread_id], start_time, end_time, frequency, adjust)
            for thread_id in range(len(stock_list))]

        count = 0
        totallen = 0
        total = 0
        start_time = time.time()
        for future in concurrent.futures.as_completed(futures):
            count += 1
            # 函数运行抛出的异常处理
            try:
                result = future.result()
            except Exception as e:
                print(f"Exception occurred: {e}")
            else:
                totallen += result[0]
                total += result[1]
                if time.time() - start_time > 1:
                    progress = count / len(futures) * 100
                    print(f'Progress: {progress:.2f}%, Count: {totallen}, Total: {total}')
                    start_time = time.time()
