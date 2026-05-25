from datetime import datetime
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
import re
import subprocess
from time import sleep
import os
from urllib.parse import quote
import csv
import shutil

def main():
 ymd,md=get_now_day()
 check_array=get_data(md)

 #テスト用コード
 check_array.append("0525")

 if md in check_array:
    make_dir(ymd)
    #subprocess.run("C:\\Users\\dev-w\\Desktop\\workspace\\AI\\Visual_Studio\\race_time_and_odds_key.bat") #開催情報を取得する.exeを実行
    #power automateを呼び出してダイヤログを消す
    #call_Power_automate_desktop()
    #取得したcsvを読み込んで時刻情報を辞書化する
    get_data_time_dict,target_file_path=read_csv(ymd)
    move_file(ymd,target_file_path) #csvファイルを移動する 
     
def get_data(md):
    day_match_1=r"(\d+)月(\d+)日"
    day_match_2=r"(\d+)/(\d+)"
    load_url="https://race.netkeiba.com/top/"
    #xpath_day="fc"
    check_path="ui-tabs-nav"
    skip_load_flag=0
    selector_flg=1
    driver=get_driver()
    get_driver_and_wait_url(skip_load_flag,driver,load_url,selector_flg,check_path)

    elements_day =driver.find_elements(By.CLASS_NAME,check_path) 
    for elem_3 in elements_day:
        day_elem=elem_3.text.split()    
    day_count=0
    check_array=[]
    while len(day_elem)>day_count:
        check_3=day_elem[day_count]
        check_3=re.search(day_match_1,check_3)
        if check_3==None:
            check_3=day_elem[day_count]
            check_3=re.search(day_match_2,check_3)
        race_month=check_3.group(1)
        race_day=check_3.group(2)
        if len(race_month)==1:
            race_month="0"+race_month
        if len(race_day)==1:
            race_day="0"+race_day
        check_4=race_month+race_day
        check_array.append(check_4)
        day_count=day_count+1
    return check_array

def get_driver():
    options = webdriver.ChromeOptions()
    options.add_argument("--headless=new")       # GUIなし
    options.add_argument("--window-size=1920,1080")
    options.add_argument("--disable-dev-shm-usage") 
    options.add_argument("--blink-settings=imagesEnabled=false")
    options.add_argument("--disable-gpu")
    options.add_argument("--no-sandbox")
    driver = webdriver.Chrome(options=options)
    return driver

def get_driver_and_wait_url(skip_load_flag,driver,load_url,selector_flg,check_path):
    attempt = 0 
    while True:
        try:
            if skip_load_flag==0:
                driver.get(load_url)
                sleep(5)
                print("url読み込み中・・・")
            sleep(1)
            if selector_flg==1:
                WebDriverWait(driver, 25,poll_frequency=0.25).until(
                    EC.presence_of_element_located((By.CLASS_NAME,check_path)))
            elif selector_flg==2:
                WebDriverWait(driver, 25,poll_frequency=0.25).until(
                    EC.presence_of_element_located((By.ID,check_path)))
            elif selector_flg==3:
                WebDriverWait(driver, 25,poll_frequency=0.25).until(
                    EC.presence_of_element_located((By.XPATH,check_path)))
            print("読み込み完了")
            selector_flg=0
            return driver
        except Exception as e:
            attempt += 1
            print(f"読み込みが失敗したのでdriverを再生成します")
            driver.quit()
            sleep(5)
            subprocess.run(["rm","-r","/home/aweqse/.cache/selenium"])
            subprocess.run(["pkill","chrome"])
            driver = get_driver()
            driver.get(load_url)
            print("ドライバの再生成が完了しました")
            return driver

def make_dir(ymd):
    folder_path="C:\\Users\\dev-w\\Desktop\\workspace\\output\\"+ymd
    os.makedirs(folder_path, exist_ok=True)

def get_now_day():
    now = datetime.now()
    day_now=int(now.day)
    month_now=int(now.month)
    year_now=int(now.year)
    #ゼロうめ処理
    if len(str(month_now)) == 1:
        month_str = "0" + str(month_now)
    else:
        month_str = str(month_now)
    if len(str(day_now)) == 1:
        day_str = "0" + str(day_now)
    else:
        day_str = str(day_now)
    ymd=str(year_now)+month_str+day_str
    md=month_str+day_str
    return ymd,md

def read_csv(ymd):

    #テスト用コード
    ymd="20260524"

    file_name=ymd+"_race_schedule.csv"
    all_data_array=[]
    before30min={}
    before10min={}
    before5min={}
    get_data_time=[]
    target_file_path="C:\\Users\\dev-w\\Desktop\\workspace\\output\\log\\race_schedule_log\\"+file_name
    with open(target_file_path, mode="r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            all_data=[row["race_key"],row["m30_target_time"],row["m10_target_time"],row["m5_target_time"]]
            all_data_array.append(all_data)
        print("全データの作成完了")
        print("データの分割開始")

        #0が30分前、1が10分前,2が5分前
        for i in all_data_array:
            #時刻が00:00:00なので末尾の:00を削除する
            race_key=i[0]
            m30_min=i[1]
            m10_min=i[2]
            m5_min=i[3]
            
            #時刻が00:00:00なので末尾の:00を削除する処理を記述する。

            if m30_min not in before30min:
                before30min[m30_min] = []
            before30min[m30_min].append(race_key,0) #末尾の0は取得フラグ、取得済みなら1に更新する
            if m10_min not in before30min:
                before30min[m10_min] = []
            before10min[m10_min].append(race_key,0) 
            if m5_min not in before30min:
                before30min[m5_min] = []
            before5min[m5_min].append(race_key,0) 

        return get_data_time,target_file_path

def move_file(ymd,target_file_path):
    dst="C:\\Users\\dev-w\\Desktop\\workspace\\output\\"+ymd
    src=target_file_path
    dst_dir = os.path.dirname(dst)
    os.makedirs(dst_dir, exist_ok=True)
    if os.path.isfile(src):
        shutil.move(src, dst)
        print("ファイルを移動しました")
    else:
        print("移動元ファイルが存在しません")
    pass

def call_Power_automate_desktop():
    flow_name = "close_dialog"
    url = f"ms-powerautomate:/console/flow/run?workflowName={quote(flow_name)}"
    os.startfile(url)
    file_path="C:\\Users\\dev-w\\Desktop\\workspace\\output\\log\\finish.log"
    while os.path.exists(file_path)==False:
        sleep(1)
    print("Power Automate Desktopの終了")
    os.remove(file_path)
    return
        

main()