import gc
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


# 下载并处理k线数据函数
# 返回字典: {'code': 股票代码, 'count': k线记录行数, 'data': k线数据}
def download_and_proc_klines_callfunc(thread_id, save_dir: str,
                                      stock_code: str, start_time: str, end_time: str,
                                      frequency: str, adjust: str,
                                      is_map: bool = True):
    # fields
    fields_param = baostock_kline_fields_dict[frequency]
    fields_param = str.join(',', fields_param)

    # frequency
    frequency_param = baostock_kline_frequency_dict[frequency]

    # adjust
    adjust_param = baostock_kline_adjust_dict[adjust]

    # 数据下载
    stock_df = download_klines_interface(stock_code, fields_param, start_time, end_time, frequency_param, adjust_param)

    # 处理空dataframe
    if len(stock_df.index) != 0:
        if is_map:
            # 数据处理
            # fields mapper
            stock_df.rename(columns=baostock_kline_fields_mapper_dict[frequency], inplace=True)
            # data mapper
            for mapper_field_key in baostock_kline_fields_data_mapper_dict:
                if mapper_field_key in stock_df.columns:
                    mapper_field_val = baostock_kline_fields_data_mapper_dict[mapper_field_key]
                    stock_df[mapper_field_key].replace(mapper_field_val, inplace=True)

    return {'code': stock_code, 'count': len(stock_df.index), 'data': stock_df}


# 下载k线
def download_klines_interface(stock_code: str, fields: str,
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
            # return result.get_data()
            data_list = []
            while (result.error_code == '0') & result.next():
                # 获取一条记录，将记录合并在一起
                data_list.append(result.get_row_data())
            return pd.DataFrame(data_list, columns=result.fields)
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


# 比较两个list是否完全一致，允许顺序不同
def compare_lists(list1, list2):
    if set(list1) == set(list2) and len(list1) == len(list2):
        return True
    return False


# baostock多线程k线下载函数
def baostock_klines_download(stock_list: list, save_dir: str,
                             start_time: str, end_time: str,
                             frequency='day', adjust='post',
                             is_map: bool = True, max_workers=8):
    # 下载log记录信息字典
    fields_names = [baostock_kline_fields_mapper_dict[frequency][field]
                    for field in baostock_kline_fields_dict[frequency]]
    download_log_dict = {'start_time': start_time,
                         'end_time': end_time,
                         'frequency': frequency,
                         'adjust': adjust,
                         'field': fields_names,
                         'downloaded_stock': [],
                         'blank_stock': [],
                         'undownload_stock': stock_list
                         }

    # 数据路径
    metadataset_name = f"{kline_store_dir_dict[frequency]}.{adjust_store_dir_dict[adjust]}"
    save_dir_param = os.path.join(save_dir, metadataset_name)
    json_save_dir = os.path.join(save_dir, f"{metadataset_name}_download_log.json")

    # 目录检查
    check_directory(save_dir_param)

    # 断点续传，增量更新(TODO)
    if os.path.exists(json_save_dir):
        print("发现上次的下载日志")

        # 打开json文件并读取
        with open(json_save_dir, 'r', encoding='utf-8') as f:
            last_download_log_dict = json.load(f)

        # 断点续传
        if last_download_log_dict['start_time'] == download_log_dict['start_time'] and last_download_log_dict[
            'end_time'] == download_log_dict['end_time'] and last_download_log_dict['frequency'] == download_log_dict[
            'frequency'] and last_download_log_dict['adjust'] == download_log_dict['adjust'] and last_download_log_dict[
            'field'] == download_log_dict['field'] and compare_lists(
            last_download_log_dict['downloaded_stock'] + last_download_log_dict['blank_stock'] +
            last_download_log_dict['undownload_stock'], download_log_dict['undownload_stock']):
            print("断点续传中")
            print(
                f"剩余股票数量:{len(last_download_log_dict['undownload_stock'])}/{len(download_log_dict['undownload_stock'])}")
            download_log_dict = last_download_log_dict

    undownload_stock_list = download_log_dict['undownload_stock']

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
            executor.submit(download_and_proc_klines_callfunc, thread_id,
                            save_dir_param, undownload_stock_list[thread_id],
                            start_time, end_time, frequency, adjust, is_map)
            for thread_id in range(len(undownload_stock_list))]

        count = 0
        pct_count = 0
        total = 0
        timer_t = time.time()
        for future in concurrent.futures.as_completed(futures):
            pct_count += 1
            try:
                result = future.result()
            except Exception as e:
                print_xml('下载进程遇到错误:' + repr(e), "error")

            # 股票有数据
            if result['count'] != 0:
                # 进度更新
                count += 1
                total += result['count']
                if time.time() - timer_t > 1:
                    progress = pct_count / len(futures) * 100
                    print_xml(f'Progress: {progress:.2f}%, Count: {count}, Total: {total}')
                    timer_t = time.time()

                # 数据存储
                store_klines(save_dir_param, result['code'], result['data'])
                # log更新
                download_log_dict['downloaded_stock'].append(result['code'])
                download_log_dict['undownload_stock'].remove(result['code'])
            # 股票无数据
            else:
                # log更新
                download_log_dict['blank_stock'].append(result['code'])
                download_log_dict['undownload_stock'].remove(result['code'])

            # log写入
            with open(json_save_dir, "w", encoding='utf-8') as outfile:
                # Use ensure_ascii=False to prevent escaping of Unicode characters
                json.dump(download_log_dict, outfile, ensure_ascii=False, indent=4)

            # GC
            del result['data']
            gc.collect()

    bs.logout()


# baostock股票基础数据下载函数, 返回所有证券代码
def baostock_stock_basic_download(save_dir: str):
    check_directory(save_dir)

    lg = bs.login()
    print_xml('login respond error_code:' + lg.error_code)
    print_xml('login respond error_msg:' + lg.error_msg)

    # 请求数据
    stock_rs = bs.query_stock_basic()
    # stock_df = stock_rs.get_data()
    if stock_rs.error_code != '0':
        raise ConnectionError(stock_rs.error_msg)
    data_list = []
    while (stock_rs.error_code == '0') & stock_rs.next():
        # 获取一条记录，将记录合并在一起
        data_list.append(stock_rs.get_row_data())
    stock_df = pd.DataFrame(data_list, columns=stock_rs.fields)
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
    stock_list = stock_df[(stock_df['证券类型'] == '股票')].set_index('证券代码')['证券名称'].to_dict()
    index_list = stock_df[(stock_df['证券类型'] == '指数')].set_index('证券代码')['证券名称'].to_dict()
    basic_info_dict = {'stock': stock_list, 'index': index_list}

    # convert the dictionary to json
    json_save_dir = os.path.join(save_dir, f"stock_list.json")
    with open(json_save_dir, "w", encoding='utf-8') as outfile:
        # Use ensure_ascii=False to prevent escaping of Unicode characters
        json.dump(basic_info_dict, outfile, ensure_ascii=False, indent=4)

    return list(stock_df['证券代码'])
