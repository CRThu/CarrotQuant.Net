from baostock_download import *
from data_directory import *

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
    test_stock_list = []

    # Loop through each string in the original array
    for string in stock_list:
        # Check if the string starts with "sz." and if the length of the new array is less than 100
        if string.startswith("sz.") and len(test_stock_list) < 100:
            # If both conditions are met, append the string to the new array
            test_stock_list.append(string)

    print("download 100 stocks klines...")
    print(test_stock_list)

    # 下载k线数据
    baostock_klines_download(test_stock_list, db_ashare_baostock_csv_dir,
                             start_time='2020-01-01',
                             end_time='2023-01-01',
                             max_workers=61)

    print_xml("k线下载已完成")
