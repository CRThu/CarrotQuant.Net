import signal

import pandas as pd
import concurrent.futures
import random
import os
import time
import json
import baostock as bs

from ashare_download_params import *
from data_directory import *
from print_xml import *


# 下载并保存k线数据函数
# 返回字典: key:股票代码, value: k线记录行数
def download_and_store_klines_callfunc(thread_id, save_dir: str,
                                       stock_code: str, start_time: str, end_time: str,
                                       frequency: str, adjust: str):
    # lg = bs.login()

    # 参数处理
    # save_dir
    save_dir_param = save_dir

    # fields
    fields_param = baostock_kline_fields_dict[frequency]
    fields_param = str.join(',', fields_param)

    # frequency
    frequency_param = baostock_kline_frequency_dict[frequency]

    # adjust
    adjust_param = baostock_kline_adjust_dict[adjust]

    # 数据下载
    stock_df = download_klines(stock_code, fields_param, start_time, end_time, frequency_param, adjust_param)

    # 处理空dataframe
    if len(stock_df.index) != 0:

        # 数据处理
        # fields mapper
        stock_df.rename(columns=baostock_kline_fields_mapper_dict[frequency], inplace=True)
        # data mapper
        for mapper_field_key in baostock_kline_fields_data_mapper_dict:
            if mapper_field_key in stock_df.columns:
                mapper_field_val = baostock_kline_fields_data_mapper_dict[mapper_field_key]
                stock_df[mapper_field_key].replace(mapper_field_val, inplace=True)

        # 数据存储
        store_klines(save_dir_param, stock_code, stock_df)

        # bs.logout()

        return {stock_code: len(stock_df.index)}
    else:
        return None


# 下载k线
def download_klines(stock_code: str, fields: str,
                    start_time: str, end_time: str,
                    frequency: str, adjust: str):
    params = {'code': stock_code, 'fields': fields, 'start_date': start_time, 'end_date': end_time,
              'frequency': frequency, 'adjustflag': adjust}
    result = download_history_klines_with_retry(**params)
    return result


# 下载一支股票k线函数(带超时重试)
def download_history_klines_with_retry(**params):
    for _ in range(10):
        try:
            result = bs.query_history_k_data_plus(**params)
            if result.error_code != '0':
                raise ConnectionError(result.error_msg)
            return result.get_data()
        except Exception as e:
            print_xml('下载遇到错误, 15秒后自动重试:' + repr(e), "warning")
            time.sleep(15)
    raise TimeoutError()


# 存储k线至csv文件
def store_klines(save_dir: str, stock_code: str, df: pd.DataFrame):
    df.to_csv(os.path.join(save_dir, f'{stock_code}.csv'), mode='w', index=False)


def init_func():
    # code to be executed by each thread
    lg = bs.login()


# baostock多线程k线下载函数
def baostock_klines_download(stock_list: list, save_dir: str,
                             start_time='1990-01-01', end_time=None,
                             frequency='day', adjust='post',
                             max_workers=8):
    # 数据路径
    metadataset_name = f"{kline_store_dir_dict[frequency]}.{adjust_store_dir_dict[adjust]}"
    save_dir_param = os.path.join(save_dir, metadataset_name)

    # 目录检查
    check_directory(save_dir_param)

    lg = bs.login()
    print_xml('login respond error_code:' + lg.error_code)
    print_xml('login respond error_msg:' + lg.error_msg)

    # 外部中断进程退出
    def signal_handler(sig, frame):
        print('外部终止进程')
        executor.shutdown(wait=False)
        sys.exit(0)

    signal.signal(signal.SIGINT, signal_handler)

    # with concurrent.futures.ThreadPoolExecutor(max_workers=max_workers) as executor:
    with concurrent.futures.ProcessPoolExecutor(max_workers=max_workers, initializer=init_func) as executor:
        # submit the tasks to the executor
        futures = [
            executor.submit(download_and_store_klines_callfunc, thread_id
                            , save_dir_param, stock_list[thread_id], start_time, end_time, frequency, adjust)
            for thread_id in range(len(stock_list))]

        count = 0
        pct_count = 0
        total = 0
        stock_log_dict = {}
        timer_t = time.time()
        for future in concurrent.futures.as_completed(futures):
            pct_count += 1
            result = future.result()
            if result is not None:
                count += 1
                stock_log_dict.update(result)
                total += list(result.values())[0]
                if time.time() - timer_t > 1:
                    progress = pct_count / len(futures) * 100
                    print_xml(f'Progress: {progress:.2f}%, Count: {count}, Total: {total}')
                    timer_t = time.time()

        # Convert my_dict1 to JSON and store it in a file named "my_dict1.json"
        if end_time is None:
            end_time = time.localtime().strftime('%Y-%m-%d')
        download_log_dict = {'start_time': start_time,
                             'end_time': end_time,
                             'frequency': frequency, 'adjust': adjust,
                             'field': baostock_kline_fields_dict[frequency], 'stock_log': stock_log_dict}

        json_save_dir = os.path.join(save_dir, f"{metadataset_name}_download_log.json")
        with open(json_save_dir, "w", encoding='utf-8') as outfile:
            # Use ensure_ascii=False to prevent escaping of Unicode characters
            json.dump(download_log_dict, outfile, ensure_ascii=False, indent=4)

    bs.logout()


# baostock股票基础数据下载函数, 返回所有证券代码
def baostock_stock_basic_download(save_dir: str):
    check_directory(save_dir)

    lg = bs.login()
    print_xml('login respond error_code:' + lg.error_code)
    print_xml('login respond error_msg:' + lg.error_msg)

    # 请求数据
    stock_rs = bs.query_stock_basic()
    stock_df = stock_rs.get_data()
    bs.logout()

    # 数据处理
    # fields mapper
    stock_df.rename(columns=baostock_stock_basic_params_mapper_dict, inplace=True)
    # data mapper
    for mapper_field_key in baostock_stock_basic_data_mapper_dict:
        if mapper_field_key in stock_df.columns:
            mapper_field_val = baostock_stock_basic_data_mapper_dict[mapper_field_key]
            stock_df[mapper_field_key].replace(mapper_field_val, inplace=True)

    stock_basic_save_dir = os.path.join(save_dir, f"stock_basic.csv")
    stock_df.to_csv(stock_basic_save_dir, index=False)

    # create a dictionary from the dataframe
    stock_list_dict = stock_df.set_index('证券代码')['证券名称'].to_dict()

    # convert the dictionary to json
    json_save_dir = os.path.join(save_dir, f"stock_list.json")
    with open(json_save_dir, "w", encoding='utf-8') as outfile:
        # Use ensure_ascii=False to prevent escaping of Unicode characters
        json.dump(stock_list_dict, outfile, ensure_ascii=False, indent=4)

    return list(stock_df['证券代码'])
