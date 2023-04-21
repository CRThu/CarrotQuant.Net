import pandas as pd
from main_download import MyClass
import os

if __name__ == '__main__':
    # os.environ["http_proxy"] = "http://127.0.0.1:7890"
    # os.environ["https_proxy"] = "http://127.0.0.1:7890"

    my_class = MyClass()
    start_time = '1990-01-01'
    end_time = '2023-04-21'

    # my_class.download_ashare(start_time=start_time, end_time=end_time, frequency='day', adjust='post')
    # my_class.download_ashare(start_time=start_time, end_time=end_time, frequency='day', adjust='none')
    my_class.download_ashare(start_time=start_time, end_time=end_time, frequency='5m', adjust='post', max_workers=16)
    my_class.download_ashare(start_time=start_time, end_time=end_time, frequency='5m', adjust='none', max_workers=16)
