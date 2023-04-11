import pandas as pd
import concurrent.futures
import random
import os
import time


# define a function to generate data
def generate_data():
    # generate data here
    return random.randint(0, 100)


# define a function to write data to dataframe
def write_to_dataframe(save_dir: str, data, thread_num):
    time.sleep(data / 1000)
    df = pd.DataFrame({'data': [data]})
    write_to_csv(save_dir, str(thread_num), df)
    return len(df.index), data


def dir_detect(save_dir: str):
    if not os.path.exists(save_dir):
        os.makedirs(save_dir)


def write_to_csv(save_dir: str, stock_code: str, df: pd.DataFrame):
    df.to_csv(os.path.join(save_dir, f'{stock_code}.csv'), mode='w', index=False)


dir_detect('datadir')
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as executor:
    # submit the tasks to the executor
    futures = [executor.submit(write_to_dataframe, 'datadir', generate_data(), i) for i in range(1000)]
    count = 0
    totallen = 0
    total = 0
    start_time = time.time()
    for future in concurrent.futures.as_completed(futures):
        count += 1
        result = future.result()
        totallen += result[0]
        total += result[1]
        if time.time() - start_time > 1:
            progress = count / len(futures) * 100
            print(f'Progress: {progress:.2f}%, Count: {totallen}, Total: {total}')
            start_time = time.time()
