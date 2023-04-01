import baostock as bs
import pandas as pd

lg = bs.login()

rs = bs.query_history_k_data_plus("sz.000422",
    "date,time,code,open,high,low,close,volume,amount,adjustflag",
    start_date='2020-01-01', end_date='2020-12-31',
    frequency="5", adjustflag="3")

data_list = []
while (rs.error_code == '0') & rs.next():
    data_list.append(rs.get_row_data())
result = pd.DataFrame(data_list, columns=rs.fields)

print(result)
bs.logout()
