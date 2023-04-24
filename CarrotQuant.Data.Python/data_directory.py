# 在此进行工程所有路径定义
# 本文件运行路径
# D:\Projects\CarrotQuant.Net\CarrotQuant.Data.Python

import os


def combine(*paths: str) -> str:
    return os.path.join(*paths)


def check_directory(path):
    if not os.path.exists(path):
        os.makedirs(path)


solution_dir: str = '..'
project_dir: str = combine(solution_dir, "CarrotQuant.Data.Python")
db_dir: str = combine(solution_dir, "CarrotQuant.Data")

if __name__ == '__main__':
    print('my_dir:\t\t\t'
          + os.getcwd())
    print(
        'solution_dir:\t'
        + solution_dir + '\t\t\t\t\t\t\t'
        + ('Exist' if os.path.exists(solution_dir) else 'Not Exist'))
    print(
        'project_dir:\t'
        + project_dir + '\t'
        + ('Exist' if os.path.exists(project_dir) else 'Not Exist'))
    print(
        'db_dir:\t\t\t'
        + db_dir + '\t\t\t'
        + ('Exist' if os.path.exists(db_dir) else 'Not Exist'))
