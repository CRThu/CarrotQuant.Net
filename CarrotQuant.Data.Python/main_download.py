# -*- coding: utf-8 -*-
import version
from baostock_download import *
from data_directory import *
import sys
import time

from print_xml import print_xml
import warnings


class MyClass:
    def get_version(self) -> None:
        print_xml(f"版本号: {version.version}", "version")

    def download_ashare(self, start_time='1990-01-01', end_time='now',
                        frequency='day', adjust='post') -> None:
        db_ashare_baostock_csv_dir: str = combine(db_dir, "ashare", "baostock", "csv")

        if end_time == 'now':
            end_time = ''

        # 下载股票信息
        print_xml(f"正在下载股票信息")
        stock_list = baostock_stock_basic_download(db_ashare_baostock_csv_dir)

        # 筛选非ETF
        # Initialize an empty list to store the selected strings
        stock_list_new = []
        for string in stock_list:
            if not string.startswith("of."):
                stock_list_new.append(string)

        # 下载k线数据
        print_xml(f"正在下载股票历史数据")
        baostock_klines_download(stock_list=stock_list_new, save_dir=db_ashare_baostock_csv_dir,
                                 start_time=start_time, end_time=end_time,
                                 frequency=frequency, adjust=adjust,
                                 max_workers=61)

        print_xml("k线下载已完成")


def main() -> None:
    override_print()
    # sys.stdout.reconfigure(encoding='utf-8')
    # Ignore FutureWarning for frame.append method
    warnings.simplefilter(action='ignore', category=FutureWarning)


    my_class = MyClass()

    if len(sys.argv) < 2:
        print_xml("Please provide a function name", "error")
    else:
        func_name = sys.argv[1]
        args = sys.argv[2:]

        try:
            func = getattr(my_class, func_name)
            func(*args)
        except AttributeError:
            print_xml("Function does not exist", "error")
        except Exception as e:
            print_xml(f"Exception occurred: {e}", "error")

    print_xml("0", "return")
    sys.exit()  # 调用则退出程序


if __name__ == '__main__':
    main()
