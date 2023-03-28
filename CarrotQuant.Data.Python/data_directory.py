# 在此进行工程所有路径定义
# 本文件运行路径
# D:\Projects\CarrotQuant.Net\CarrotQuant.Data.Python

import os

solution_dir: str = '..'
project_dir: str = os.path.join(solution_dir, 'CarrotQuant.Data.Python')
db_dir: str = os.path.join(solution_dir, 'Database')
db_ashare_csv_daily_dir: str = os.path.join(db_dir, 'ashare\\csv\\daily')


if __name__ == '__main__':
    print('my_dir:\t'
          + os.getcwd())
    print(
        'solution_dir:\t'
        + solution_dir + '\t\t\t\t'
        + ('Exist' if os.path.exists(solution_dir) else 'Not Exist'))
    print(
        'project_dir:\t'
        + project_dir + '\t'
        + ('Exist' if os.path.exists(project_dir) else 'Not Exist'))
    print(
        'db_dir:\t\t'
        + db_dir + '\t\t\t'
        + ('Exist' if os.path.exists(db_dir) else 'Not Exist'))
