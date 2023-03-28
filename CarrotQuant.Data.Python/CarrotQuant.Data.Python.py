import baostock as bs
import pandas as pd

lg = bs.login()

rs = bs.query_history_k_data_plus("sh.600000",
    "date,code,open,high,low,close,preclose,volume,amount,adjustflag,turn,tradestatus,pctChg,isST",
    start_date='2020-01-01', end_date='2020-12-31',
    frequency="d", adjustflag="3")

data_list = []
while (rs.error_code == '0') & rs.next():
    data_list.append(rs.get_row_data())
result = pd.DataFrame(data_list, columns=rs.fields)

print(result)
bs.logout()
