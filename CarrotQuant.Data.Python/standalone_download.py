from main_download import MyClass

if __name__ == '__main__':
    my_class = MyClass()
    my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='day', adjust='post')
    my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='day', adjust='none')
    my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='5min', adjust='post')
    my_class.download_ashare(start_time='1990-01-01', end_time='now', frequency='5min', adjust='none')