from baostock_download import *
from data_directory import *


def stock_filter(input_array, prefix: str, code_start: int, code_end: int):
    # Split the string array by '.'
    split_array = [s.split('.') for s in input_array]

    # Filter the strings that start with 'sz.' and have a number between 000400 and 000430 after the dot
    filtered_array = ['.'.join(s) for s in split_array if
                      s[0] == prefix
                      and len(s) == 2
                      and s[1].isdigit()
                      and int(s[1]) >= code_start and int(
                          s[1]) <= code_end]
    return filtered_array


if __name__ == '__main__':
    # override_print()

    db_ashare_baostock_csv_dir: str = combine(db_dir, "ashare", "baostock", "csv.test")
    print(
        'db_ashare_baostock_csv_dir:\t\t\t'
        + db_ashare_baostock_csv_dir + '\t\t\t'
        + ('Exist' if os.path.exists(db_ashare_baostock_csv_dir) else 'Not Exist'))

    # 下载股票信息
    stock_list = baostock_stock_basic_download(db_ashare_baostock_csv_dir)

    # 筛选5个股票
    # Initialize an empty list to store the selected strings
    # test_stock_list = []

    # # Loop through each string in the original array
    # for string in stock_list:
    #     # Check if the string starts with "sz." and if the length of the new array is less than 100
    #     if string.startswith("sz.") and len(test_stock_list) < 150:
    #         # If both conditions are met, append the string to the new array
    #         test_stock_list.append(string)
    # print("download 100 stocks klines...")

    test_stock_list = stock_filter(stock_list, 'sz', 400, 430)

    print(test_stock_list)

    # 下载k线数据
    baostock_klines_download(test_stock_list, db_ashare_baostock_csv_dir,
                             start_time='2022-01-01',
                             end_time='2023-01-01',
                             frequency='5m', adjust='post',
                             max_workers=16, is_map=True)

    print_xml("k线下载已完成")
