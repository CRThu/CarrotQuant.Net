"""
k线频率对应baostock frequency参数名
frequency：数据类型，默认为d，日k线；d=日k线、w=周、m=月、5=5分钟、15=15分钟、30=30分钟、60=60分钟k线数据，不区分大小写；
指数没有分钟线数据；周线每周最后一个交易日才可以获取，月线每月最后一个交易日才可以获取。
"""
baostock_kline_frequency_dict = {
    'day': 'd',
    '5m': '5'
}

"""
复权类型
adjustflag：复权类型，默认不复权：3；1：后复权；2：前复权。
已支持分钟线、日线、周线、月线前后复权。 BaoStock提供的是涨跌幅复权算法复权因子。
"""
baostock_kline_adjust_dict = {
    'none': '3',
    'backward': '1',
    'forward': '2'
}

"""
fields：指示简称，支持多指标输入，以半角逗号分隔，填写内容作为返回类型的列。
详细指标列表见历史行情指标参数章节，日线与分钟线参数不同。
日线指标参数（包含停牌证券）：
参数名称   参数描述         说明
date      交易所行情日期   格式：YYYY-MM-DD
code      证券代码         格式：sh.600000。sh：上海，sz：深圳
open      今开盘价格       精度：小数点后4位；单位：人民币元
high      最高价           精度：小数点后4位；单位：人民币元
low       最低价           精度：小数点后4位；单位：人民币元
close     今收盘价         精度：小数点后4位；单位：人民币元
preclose  昨日收盘价       精度：小数点后4位；单位：人民币元
volume    成交数量         单位：股
amount    成交金额         精度：小数点后4位；单位：人民币元
adjustflag复权状态         不复权、前复权、后复权
turn      换手率           精度：小数点后6位；单位：%
tradestatus交易状态       1：正常交易 0：停牌
pctChg    涨跌幅（百分比） 精度：小数点后6位
peTTM     滚动市盈率       精度：小数点后6位
psTTM     滚动市销率       精度：小数点后6位
pcfNcfTTM 滚动市现率       精度：小数点后6位
pbMRQ     市净率           精度：小数点后6位
isST      是否ST           1是，0否

5、15、30、60分钟线指标参数(不包含指数):
参数名称    参数描述        说明
date        交易所行情日期    格式：YYYY-MM-DD
time        交易所行情时间    格式：YYYYMMDDHHMMSSsss
code        证券代码        格式：sh.600000。sh：上海，sz：深圳
open        开盘价格        精度：小数点后4位；单位：人民币元
high        最高价        精度：小数点后4位；单位：人民币元
low         最低价        精度：小数点后4位；单位：人民币元
close       收盘价        精度：小数点后4位；单位：人民币元
volume      成交数量        单位：股
amount      成交金额        精度：小数点后4位；单位：人民币元
adjustflag  复权状态        不复权、前复权、后复权

周、月线指标参数：
参数名称	参数描述	说明			算法说明
date	交易所行情日期	格式：YYYY-MM-DD		
code	证券代码		格式：sh.600000。sh：上海，sz：深圳	
open	开盘价格		精度：小数点后4位；单位：人民币元	
high	最高价		精度：小数点后4位；单位：人民币元	
low	    最低价		精度：小数点后4位；单位：人民币元	
close	收盘价		精度：小数点后4位；单位：人民币元	
volume	成交数量		单位：股		
amount	成交金额		精度：小数点后4位；单位：人民币元	
adjustflag	复权状态	不复权、前复权、后复权	
turn	换手率		精度：小数点后6位；单位：%	
pctChg	涨跌幅（百分比）	精度：小数点后6位	涨跌幅=[(区间最后交易日收盘价-区间首个交易日前收盘价)/区间首个交易日前收盘价]*100%

"""
baostock_kline_fields_dict = {
    'day': ['date', 'code', 'open', 'high', 'low', 'close', 'preclose', 'volume', 'amount', 'adjustflag', 'turn',
            'tradestatus', 'pctChg', 'peTTM', 'psTTM', 'pcfNcfTTM', 'pbMRQ', 'isST'],
    '5m': ['date', 'time', 'code', 'open', 'high', 'low', 'close', 'volume', 'amount', 'adjustflag']
}

"""
baostock fields名映射字典

"""
baostock_kline_fields_mapper_dict = {
    'day': {
        'date': '交易日期',
        'code': '证券代码',
        'open': '开盘价',
        'high': '最高价',
        'low': '最低价',
        'close': '收盘价',
        'preclose': '昨收价',
        'volume': '成交量',
        'amount': '成交额',
        'adjustflag': '复权状态',  # 1：后复权， 2：前复权，3：不复权
        'turn': '换手率',
        'tradestatus': '交易状态',  # 1：正常交易 0：停牌
        'pctChg': '涨跌幅',
        'peTTM': '滚动市盈率',
        'psTTM': '滚动市销率',
        'pcfNcfTTM': '滚动市现率',
        'pbMRQ': '市净率',
        'isST': '是否ST',  # 1是，0否
    },
    '5m': {
        'date': '交易日期',
        'time': '交易时间',
        'code': '证券代码',
        'open': '开盘价',
        'high': '最高价',
        'low': '最低价',
        'close': '收盘价',
        'volume': '成交量',
        'amount': '成交额',
        'adjustflag': '复权状态',  # 1：后复权， 2：前复权，3：不复权
    }
}

"""
baostock fields数据映射字典

"""
baostock_kline_fields_data_mapper_dict = {
    '复权状态': {
        '1': '后复权',
        '2': '前复权',
        '3': '不复权'
    },
    '交易状态': {
        '1': '正常交易',
        '0': '停牌'
    },
    '是否ST': {
        '1': '是',
        '0': '否'
    }
}

"""
k线频率对应存储路径文件夹名
"""
kline_store_dir_dict = {
    'day': 'daily',
    '5m': '5min'
}

"""
baostock stock_basic参数名映射字典
参数名称   参数描述
code      证券代码
code_name 证券名称
ipoDate   上市日期
outDate   退市日期
type      证券类型，其中1：股票，2：指数，3：其它，4：可转债，5：ETF
status    上市状态，其中1：上市，0：退市
"""
baostock_stock_basic_params_mapper_dict = {
    'code': '证券代码',
    'code_name': '证券名称',
    'ipoDate': '上市日期',
    'outDate': '退市日期',
    'type': '证券类型',  # 1：股票, 2：指数, 3：其它
    'status': '上市状态',  # 1：上市, 0：退市
}

"""
baostock stock_basic参数数据映射字典
参数名称   参数描述
type      证券类型，其中1：股票，2：指数，3：其它，4：可转债，5：ETF
status    上市状态，其中1：上市，0：退市
"""
baostock_stock_basic_data_mapper_dict = {
    '证券类型': {
        '1': '股票',
        '2': '指数',
        '3': '其他',
        '4': '可转债',
        '5': 'ETF'
    },
    '上市状态': {
        '1': '上市',
        '0': '退市'
    }
}
