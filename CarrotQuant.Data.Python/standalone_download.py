import pandas as pd
from main_download import MyClass
import os

if __name__ == '__main__':
    # os.environ["http_proxy"] = "http://127.0.0.1:7890"
    # os.environ["https_proxy"] = "http://127.0.0.1:7890"

    my_class = MyClass()
    # my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='day', adjust='post')
    # my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='day', adjust='none')
    my_class.download_ashare(start_time='2010-01-01', end_time='now', frequency='5m', adjust='post')
    # my_class.download_ashare(start_time='2010-01-01', end_time='now', frequency='5m', adjust='none')
