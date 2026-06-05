from pathlib import Path
import config
import csv
import mysql.connector
from datetime import date
import shutil
import math
from email.mime.text import MIMEText
import base64
from googleapiclient.discovery import build
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
import os
import subprocess

#-------20260605-------
#これからやること
#①jockey_infoをtrainer_infoを手本に一連の処理を完成させる
#②出力、修正したファイルを全て~csvファイルに移動するように処理を変更する
#③全てのファイルをサーバーに転送させる処理を追加する
#④trainer_info,jockey_infoを一度insertする
#⑥差分(2026年分)のcsvを読み込ませせてupdate処理とファイル転送の処理の確認
#⑦全体を管理するサーバーとスクリプトを書いて自動的にVMの起動、スクリプトの実行と一連の処理がうまく行くか試す.

def main():
    now= date.today().strftime("%Y%m%d")
    conn,cursor=connect_mysql()
    # make_dir()
    # insert_race_result_csv_dir=config.insert_race_result_csv_dir
    # insert_odds_csv_dir=config.insert_odds_csv_dir
    # insert_horse_csv_dir=config.insert_horse_csv_dir
    # race_result_filenam_array=race_result_get_filename(insert_race_result_csv_dir)
    # horse_array=horsde_get_filename(insert_horse_csv_dir)
    # odds_array=odds_get_filename(insert_odds_csv_dir)
    # print("フォルダ内の初期のファイル名の取得完了")

    # while len(race_result_filenam_array)!=0:
    #     #csvファイルは一つしかない想定なので[0]固定
    #     target_file=race_result_filenam_array[0]
    #     target_file_path=insert_race_result_csv_dir+"\\"+target_file
    #     target_dict,fixed_flag=check_data(target_file_path)
    #     if fixed_flag!=1:
    #       from_dict_to_converted_array=convert_form_dict_to_list(target_dict)   
    #       make_csv(target_file_path,from_dict_to_converted_array)
    #       fixed_flag=0

    # #mysqlのdump（バックアップを取得処理を追加する）
    # dump_mysql()
           
    # insert_race_result(from_dict_to_converted_array,conn,cursor)
    # race_result_filenam_array=race_result_get_filename(insert_race_result_csv_dir)
    # move_file(insert_race_result_csv_dir,race_result_filenam_array)

    # #馬情報の処理
    # target_file=horse_array[0]
    # target_file_path=insert_horse_csv_dir+"\\"+target_file
    # insert_horse(target_file_path,conn,cursor)
    # move_file(target_file_path)

    # #オッズ情報の処理
    # target_file=odds_array[0]
    # target_file_path=insert_odds_csv_dir+"\\"+target_file
    # insert_odds(target_file_path,target_file,conn,cursor)
    # move_file(target_file_path)

    #trainer_infoの処理
    insert_array,updata_array=make_trainer_data(cursor)
    if len(insert_array)!=0:
        target_file_path=config.output_csv+"\\trainer_info_insert.csv"
        fixed_flag=3
        make_csv_array=insert_array
        make_csv(target_file_path,fixed_flag,make_csv_array)
        insert_array=make_csv_array
        insert_trainer_info(insert_array,conn,cursor)
    elif len(updata_array)!=0:
        target_file_path=config.output_csv+"//trainer_info_update.csv"
        fixed_flag=3
        make_csv_array=updata_array
        make_csv(target_file_path,fixed_flag,make_csv_array)
        updata_array=make_csv_array
        updata_trainer_info(updata_array,cursor)

    #jockey_infoの処理
    convert_form_dict_to_list()
    insert_jockey_info()

    #すべてのcsvをファイルサーバーに移動する
    export_csv_to_fileserver()
    print("すべての処理完了！！")
    
def connect_mysql():
    sql_pass=config.sql_pass
    # DB接続 
    conn = mysql.connector.connect(
        host="192.168.1.108",
        user="keiba",
        password=sql_pass,
        database="keiba"
    )
    cursor = conn.cursor(dictionary=True)
    print("データベースの接続完了")
    return conn,cursor

def race_result_get_filename(insert_race_result_csv_dir):
    #初期変数設定
    race_result_array=[]
    #race_resultを出力するフォルダからcsvファイルの一覧を取得する。
    folder_path = Path(insert_race_result_csv_dir)
    files = [p.name for p in folder_path.iterdir() if p.is_file()]
    for file_name in files:
        if "race_result_" in file_name and not("~lock" in file_name):
            race_result_array.append(file_name)
    return race_result_array

def odds_get_filename(insert_odds_csv_dir):
    #oddsを出力するフォルダからcsvファイルの一覧を取得する。
    odds_array=[]
    folder_path = Path(insert_odds_csv_dir)
    files = [p.name for p in folder_path.iterdir() if p.is_file()]
    for file_name in files:
        if "odds_" in file_name and not("~lock" in file_name):
            odds_array.append(file_name)
    return odds_array

def horsde_get_filename(insert_horse_csv_dir):
    #horseを出力するフォルダからcsvファイルの一覧を取得する。
    horse_array=[]
    folder_path = Path(insert_horse_csv_dir)
    files = [p.name for p in folder_path.iterdir() if p.is_file()]
    for file_name in files:
        if "horse_" in file_name and not("~lock" in file_name):
            horse_array.append(file_name)
    return horse_array

#レース情報の処理
def insert_race_result(for_insert_and_make_csv_array,conn,cursor):
    insert_query="insert into race_result values (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
    cursor.executemany(insert_query, for_insert_and_make_csv_array)
    conn.commit()
    print("レース結果をコミットしました。")

#馬情報の処理
def insert_horse(target_file_path,conn,cursor):
    insert_horse_array=[]
    update_horse_array=[]
    run_row_dict={}
    judge_data_dict={}
    jugde_dict_count=0
    run_row_dict_count=0
    today = date.today()

    #insertかupdateか判断するための配列を取得
    judge_query="select horse_id,sex,trainer_name,trainer_id,owner_name,owner_id,final_run_day,reject_jra_date,trainer_belong_area from horse_info;"
    cursor.execute(judge_query)
    judge_data_array = cursor.fetchall()
    print("差分比較用の情報取得完了")

    #DBの内容を辞書化する
    while len(judge_data_array)>jugde_dict_count:
        horse_id=int(judge_data_array[jugde_dict_count]["horse_id"])
        judge_data_dict[horse_id]={"sex":judge_data_array[jugde_dict_count]["sex"],"trainer_name":judge_data_array[jugde_dict_count]["trainer_name"],"trainer_id":judge_data_array[jugde_dict_count]["trainer_id"],
                                   "owner_name":judge_data_array[jugde_dict_count]["owner_name"],"owner_id":judge_data_array[jugde_dict_count]["owner_id"],
                                   "final_run_day":judge_data_array[jugde_dict_count]["final_run_day"],"reject_jra_date":judge_data_array[jugde_dict_count]["reject_jra_date"],
                                   "trainer_belong_area":judge_data_array[jugde_dict_count]["trainer_belong_area"]}

        jugde_dict_count=jugde_dict_count+1
    print("差分比較用の辞書の作成完了")

    run_day_query="select horse_id,min(year*10000+month*100+day)AS first_run_day,max(year*10000+month*100+day)AS final_run_day from race_result group by horse_id;"
    cursor.execute(run_day_query)
    run_row = cursor.fetchall()
    print("出走日に関する情報取得完了")

    while len(run_row)>run_row_dict_count:
        horse_id=int(run_row[run_row_dict_count]["horse_id"])
        run_row_dict[horse_id]={"first_run_day":run_row[run_row_dict_count]["first_run_day"],"final_run_day":run_row[run_row_dict_count]["final_run_day"]}
        run_row_dict_count=run_row_dict_count+1
    print("出走日用の辞書を作成完了")

    with open(target_file_path, mode="r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            data=[int(row["horse_id"]),row["horse_name"],int(row["sex_cd"]),int(row["birthday"]),row["trainer_raw"],int(row["trainer_id"]),row["breed_raw"],
                  int(row["breed_id"]),row["owner_raw"],int(row["owner_code"]),row["regist_jra_date"],row["reject_jra_date"],int(row["tozai_cd"]),
                  row["father_raw"],int(row["father_code"]),row["mother_raw"],int(row["mother_code"])]    
            print("DBに登録する値の取得完了")

            #更新日を追加する
            last_update = int(today.strftime("%Y%m%d"))
            
            #配列に追加する
            horse_id=int(row["horse_id"])
            if  horse_id in run_row_dict:
                first_run_day=run_row_dict[horse_id]["first_run_day"]
                final_run_day=run_row_dict[horse_id]["final_run_day"]
                data.insert(11,first_run_day)
                data.insert(12,final_run_day)
                data.append(last_update)
                print("初出走日と最終出走日の取得が完了しました。")
            else:
                data.insert(11,None)
                data.insert(12,None)
                data.append(last_update)
                print("対象の馬はいませんでした")

                #変更箇所に差分があるかのチェックのためのデータ生成
            if  horse_id in judge_data_dict:
                judge_data=[str(judge_data_dict[horse_id]["sex"]),str(judge_data_dict[horse_id]["trainer_name"]),str(judge_data_dict[horse_id]["trainer_id"]),str(judge_data_dict[horse_id]["owner_name"]),
                            str(judge_data_dict[horse_id]["owner_id"]),str(judge_data_dict[horse_id]["final_run_day"]),str(judge_data_dict[horse_id]["reject_jra_date"]),
                            str(judge_data_dict[horse_id]["belong_area"])]
                            
                #csvファイルから比較用のデータ_2を作成する
                compare_data=[str(data[2]),str(data[4]),str(data[5]),str(data[8]),str(data[9]),str(data[12]),str(data[13]),str(data[14])]
                data=[data[2],data[4],data[5],data[8],data[9],data[12],data[13],data[14]]

                #データ_1とデータ_2を比較して差分があればupdate処理用の配列に更新値を格納する、何もなければ処理は何もしない
                if judge_data!=compare_data:
                    data.append(last_update)
                    data.append(int(row["horse_id"]))

                    #nullをnoneに変換す
                    data=convert_null(data)
                    
                    update_horse_array.append(data)
                    print("アップデート配列にデータの格納完了")
                else:
                    print("差分はないので何も処理はしない")
            else:
                print("差分検出用データーがないのでインサート処理に移行")
                
                #nullをnoneに変換する
                data=convert_null(data)

                insert_horse_array.append(data)
                print("インサート配列にデータの格納完了")

        if len(insert_horse_array)!=0:
            insert_query="insert into horse_info values (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_query, insert_horse_array)
        
        if len(update_horse_array)!=0:
            updata_query="update horse_info set sex=%s,trainer_name=%s,trainer_id=%s,owner_name=%s,owner_id=%s,final_run_day=%s,reject_jra_date=%s,belong_area=%s,last_update=%s where horse_info.horse_id=%s;"      
            cursor.executemany(updata_query, update_horse_array)

        conn.commit()
        print("コミット完了")

#オッズの処理
def insert_odds(target_file_path,target_file,conn,cursor):
    #race_resultから発送時刻と識別するためのrace_idを取得する。
        start_race_time_dict={}
        start_race_time_count=0
        insert_odds_array=[]
        total_array=[]

        start_race_time_query="select race_id,start_race_time from race_result;"
        cursor.execute(start_race_time_query)
        start_race_time_array = cursor.fetchall()
        print("発送時刻を取得完了")
        
        with open(target_file_path, mode="r", encoding="shift_jis", newline="") as f:
            reader = csv.DictReader(f)
            for row in reader:
                race_id_raw = row["レースID"]
                year = race_id_raw[0:4]
                place = race_id_raw[8:10]
                kai = race_id_raw[10:12]
                nitime = race_id_raw[12:14]
                race_number = race_id_raw[14:16]
                race_id=year+place+kai+nitime+race_number
                race_id=int(race_id)
                
                start_race_time_dict[race_id]={"race_id":start_race_time_array[start_race_time_count]["race_id"],"start_race_time":start_race_time_array[start_race_time_count]["start_race_time"]}
                start_race_time_count=start_race_time_count+1
                print("辞書の作成完了")

                if "before5" in target_file:
                    snapshot_min = 5
                elif "before10" in target_file:
                    snapshot_min = 10
                elif "before30" in target_file:
                    snapshot_min = 30
                elif "after" in target_file:
                    snapshot_min = 0
                print("取得時刻の分類の取得完了")

                #start_race_time
                start_race_time=start_race_time_dict[race_id]["start_race_time"]

                #gettimeの取得
                get_time_raw = row["月日時分"]
                get_time=get_time_raw[4:]
                get_time_hour=get_time[:2]
                get_time_minute=get_time[2:]
                get_time=get_time_hour+":"+get_time_minute
                print("取得時刻の取得完了")

                #共通するカラムの変数を配列に格納する
                if "win_and_fuku" in target_file:
                    insert_odds_array=win_and_fuku_processing(insert_odds_array,row,race_id,snapshot_min,start_race_time,get_time)
                elif "wide" in target_file:
                    ticket_type=3
                elif "umaren" in target_file:
                    ticket_type=4
                    insert_odds_array=umaren_processing(insert_odds_array,row,race_id,snapshot_min,start_race_time,get_time,ticket_type)
                elif "umatan" in target_file:
                    ticket_type=5
                elif "sanrenpuku" in target_file:
                    ticket_type=6
                elif "sanrentan" in target_file:
                    ticket_type=7
                
                #５万行ごとにインサート処理する。
                total_array.extend(insert_odds_array)
                insert_odds_array=[]
                if len(total_array)>10000:
                    insert_query="insert into race_odds values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
                    cursor.executemany(insert_query, total_array)
                    conn.commit()
                    total_array=[]
                    print("暫定コミット完了")
                    
            
            #あふれた行をインサートする
            insert_query="insert into race_odds values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_query, total_array)
            conn.commit()
            print("コミット完了")

def win_and_fuku_processing(insert_odds_array,row,race_id,snapshot_min,start_race_time,get_time):
    umaban2=0
    umaban3=0
    umaban_count=1
    
    #最大出走頭数が18頭だから19>にする
    while 19>umaban_count:
        win_search_str=str(umaban_count)+"単"
        min_fuku_serach_odds=str(umaban_count)+"複Lo"
        max_fuku_search_odds=str(umaban_count)+"複Hi"
        if row[str(win_search_str)]!=None:
            #単勝の場合
            ticket_type=1
            umaban1=umaban_count
            odds=float(row[win_search_str])
            min_odds=0
            max_odds=0

            if odds==0:
                odds_log=0
            else:
                odds_log=math.log(odds)

            min_odds_log=0
            max_odds_log=0
            odds_array=[race_id,snapshot_min,start_race_time,get_time,ticket_type,umaban1,umaban2,umaban3,odds,min_odds,max_odds,odds_log,min_odds_log,max_odds_log]
            insert_odds_array.append(odds_array)
            
            #複勝の場合
            ticket_type=2
            odds=0
            min_odds=float(row[min_fuku_serach_odds])
            max_odds=float(row[max_fuku_search_odds])
            odds_log=0

            if min_odds==0:
                min_odds_log=0
            else:
                min_odds_log=math.log(min_odds)

            if max_odds==0:
                max_odds_log=0
            else:
                max_odds_log=math.log(max_odds)

            odds_array=[race_id,snapshot_min,start_race_time,get_time,ticket_type,umaban1,umaban2,umaban3,odds,min_odds,max_odds,odds_log,min_odds_log,max_odds_log]
            insert_odds_array.append(odds_array)
        umaban_count=umaban_count+1
    return insert_odds_array
    
def umaren_processing(insert_odds_array,row,race_id,snapshot_min,start_race_time,get_time,ticket_type):
    umaban3=0
    min_odds=0
    max_odds=0
    min_odds_log=0
    max_odds_log=0
    entry=int(row["頭数"])
    umaban_count=0

    #馬番を生成する
    horse_number_array=list(range(1,entry+1))

    #初期値
    umaban1=horse_number_array[umaban_count]
    umaban2=horse_number_array[umaban_count+1]

    #umaban1を基準に馬番２は一を足した数で限度はentryまで
    while True:
        int_umaban2=umaban2
        if len(str(umaban1))==1: 
            umaban1="0"+str(umaban1)
        if len(str(umaban2))==1:
            umaban2="0"+str(umaban2)
        search_styr="馬"+str(umaban1)+"-"+str(umaban2)
         
        odds=float(row[search_styr])
        if odds==0:
            umaban2=int(umaban2)+1
            #オッズが0の場合はスキップする
            if int_umaban2==entry:
                umaban_count=0
                #配列を削除して馬連なので重複がないようにする
                del horse_number_array[umaban_count]
                if len(horse_number_array)==1:
                    #馬連なので出走馬が一頭になったら全パターン取れたことになるので配列を返す
                    return insert_odds_array
                umaban1=horse_number_array[umaban_count]
                umaban2=horse_number_array[umaban_count+1]
            continue
        else:
            odds_log=math.log(odds)

        odds_array=[race_id,snapshot_min,start_race_time,get_time,ticket_type,int(umaban1),int(umaban2),umaban3,odds,min_odds,max_odds,odds_log,min_odds_log,max_odds_log]
        insert_odds_array.append(odds_array)

        if int_umaban2==entry:
            umaban_count=0
            
            #配列を削除して馬連なので重複がないようにする
            del horse_number_array[umaban_count]
            if len(horse_number_array)==1:
                #馬連なので出走馬が一頭になったら全パターン取れたことになるので配列を返す
                return insert_odds_array
            umaban1=horse_number_array[umaban_count]
            umaban2=horse_number_array[umaban_count+1]
        else:
            umaban2=int(umaban2)+1
     
def convert_null(data):
    seach_count=0
    while len(data)>seach_count:
        target_value=data[seach_count]
        if target_value=="NULL" or target_value=="":
            data[seach_count]=None
            print("NULLの変換完了")
        seach_count=seach_count+1
    return data
        
def move_file(race_result_filenam_array,insert_race_result_csv_dir):
    #転送先を〜csvフォルダにする
    dest_path_dir=config.processed_file_path
    #送信元のパスと宛先のパスを生成する
    for r in race_result_filenam_array:
        sorce_path=Path(insert_race_result_csv_dir+"\\"+r)
        dest_path=Path(dest_path_dir+"\\"+r)
        if dest_path.exists():
            dest_path.unlink()
        shutil.move(sorce_path,dest_path)
    print("csvファイルの移動が完了しました")

def make_trainer_data(cursor):
    trainer_info_dict={}
    race_result_dict={}
    insert_array=[]
    trainer_id_array=[]
    updata_array=[]
    now= date.today().strftime("%Y%m%d")

    #race_resultから更新候補の一覧を取得する
    make_dict_query="select trainer_id,trainer,trainer_belong_area,min(year*10000+month*100+day) as first_run,max(year*10000+month*100+day) as last_run from race_result group by trainer_id,trainer,trainer_belong_area;"
    cursor.execute(make_dict_query)
    race_result_array = cursor.fetchall()

    #辞書を作成する
    if len(race_result_array)!=0:
        for row in race_result_array:
            trainer_id=row["trainer_id"]
            trainer_id_array.append(trainer_id)
            race_result_data=[row["trainer_id"],row["trainer"],row["trainer_belong_area"],row["first_run"],row["last_run"]]
            race_result_dict[row["trainer_id"]]=race_result_data
        print("比較元の辞書作成完了")

    #trainer_infoから更新候補と比較して差分があればupdate処理、noneならinsert処理に分岐する
    compare_count_query="select trainer_id,trainer_belong_area,last_run from trainer_info;"
    cursor.execute(compare_count_query)
    trainer_info_array = cursor.fetchall()

    #activeは全部のカラムが対象なので辞書とは別に全部の要素を配列に入れて全配列チェックする
    if len(trainer_info_array)!=0:
        for row in trainer_info_array:
            #updateの可能性があるので全部の要素を辞書に入れる
            trainer_info_data=[row["trainer_id"],row["trainer_name"],row["trainer_belong_area"],row["belong_update"],row["active"],row["first_run"],row["last_run"]]
            trainer_info_dict[row["trainer_id"]]=trainer_info_data
        print("比較先の辞書作成完了")

    #辞書からトレーナーIDが存在しない場合noneを返す
    for trainer_id in trainer_id_array:
        value=trainer_info_dict.get(trainer_id,None)

        #trainer_infoのtraner_idが辞書が該当すれば比較してupdate処理、なければinsert処理
        #insert処理
        if value==None:
            trainer_name=race_result_dict[trainer_id][1]
            belong_area=race_result_dict[trainer_id][2]
            belong_update=None
            active=1
            first_run=race_result_dict[trainer_id][3]
            last_run=race_result_dict[trainer_id][4]
            data=[trainer_id,trainer_name,belong_area,belong_update,active,first_run,last_run]
            #trainer_idで重複チェックする
            if data in insert_array:
                continue
            else:
                insert_array.append(data)
                continue
       
        #trainer_infoの配列をactiveとbelong_areaをCheckして当てはまればかきかえる。
        #書き換えたら必ず､差分がでるのでupdata.arrayrrに格納する
        #書き替えがない場合は比較して差分があればupdata.arraに格納する。
        if race_result_dict[trainer_id]!=trainer_info_dict[trainer_id]:
            # 初期値
            active=trainer_info_dict[trainer_id]["active"]     
            belong_area=trainer_info_dict[trainer_id]["belong_area"]
            belong_update=trainer_info_dict[trainer_id]["belong_update"]
            last_run=trainer_info_dict[trainer_id]["last_run"]

             #active_checkのチェック 
            if int(race_result_dict[trainer_id]["last_run"])+20000>now:
                active=0
            #belong_area_check       
            elif trainer_info_dict[trainer_id]["belong_area"]!=race_result_dict[trainer_id]["belong_area"]:
                belong_area=race_result_dict[trainer_id]["belong_area"]
                belong_update=now
            elif trainer_info_dict[trainer_id]["last_run"]!=race_result_dict[trainer_id]["last_run"]:
                last_run=race_result_dict[trainer_id]["last_run"]
            data=[active,belong_area,belong_update,last_run,trainer_id]
            #trainer_idで重複チェックする
            if data in insert_array:
                continue
            else:
                updata_array.append(data)
    return insert_array,updata_array
        
def insert_trainer_info(insert_array,conn,cursor):
    #インサート処理
    insert_sql="insert into trainer_info(trainer_id,trainer_name,trainer_belong_area,belong_update,active,firist_run,last_run) values(%s,%s,%s,%s,%s,%s,%s)"
    cursor.executemany(insert_sql, insert_array)
    conn.commit()
    print("インサート処理が完了しました")

def updata_trainer_info(updata_array,conn,cursor):
    #updata処理
    updata_query="updata trainer_info set active=%s,belong_area=%s,belong_update=%s,last_run=%s where trainer_id=%s;"
    cursor.executemany(updata_query, updata_array)
    conn.commit()
    print("アップデート処理が完了しました。")

def insert_jockey_info():
    pass

def check_data(target_file_path):
    race_result_inssert_data={}
    race_id_array=[]
    check_data_dict={}
    check_array_count=0
    duble_array=[]
    sent_mail_array=[]
    fixed_flag=0
    
    #検査用の辞書とrace_idの配列を作成する
    with open(target_file_path, mode="r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            #インサート用の辞書を作成
            data=[row["race_id"],row["year"],row["month"],row["day"],row["weekday"],row["kai"],row["nitime"],row["race_number"],
                row["race_name"],row["place"],row["course_distance"],row["track"],row["course_type"],row["horseage_conditions"],
                row["race_class"],row["grade"],row["weight_type"],row["only_hinba"],row["weather"],row["turf_condition"],row["dirt_condition"],
                row["start_race_time"],row["entry"],row["wakuban"],row["umaban"],row["horse_name"],row["horse_id"],row["sex"],row["horse_age"],
                row["horse_weight"],row["horse_weight_increase"],row["carried_weight"],row["jockey"],row["jockey_id"],row["jockey_belong_area"],
                row["belong_area"],row["trainer"],row["trainer_id"],row["trainer_belong_area"],row["abnormal_code"],row["rank"],row["race_time"],row["corner_1_rank"],
                row["corner_2_rank"],row["corner_3_rank"],row["corner_4_rank"],row["last_3_furlong_time"],row["time_lag"]]
            
            #nullチェックをする
            data=convert_null(data)
            
            race_id=row["race_id"]
            if race_id not in race_result_inssert_data:
                race_result_inssert_data[race_id] = []
            race_result_inssert_data[race_id].append(data)
            race_id_array.append(race_id)

            #チェック用の辞書作成
            check_data=[row["race_id"],row["rank"],row["umaban"],row["entry"]]
            if race_id not in check_data_dict:
                check_data_dict[race_id] = []
            check_data_dict[race_id].append(check_data)
            check_data=[]

        #race_id_arrayの重複を削除する
        race_id_array=list(set(race_id_array))

        #rankの値を検査する
        while len(race_id_array)>check_array_count:
            race_id=race_id_array[check_array_count]
            rank_cheak_array=check_data_dict[race_id]
            
            #重複チェックのために配列を作成する
            for duble in rank_cheak_array[2]:
                duble_array.append(duble)
            
            #rankがすべて0の場合、配列を削除する
            if all(int(row[1])==0 for row in rank_cheak_array):
                del race_result_inssert_data[race_id]
                fixed_flag=1
            
            #umabanの重複がある場合、手動でインサートするため配列からは削除する。頭数も修正
            elif len(rank_cheak_array)!=int(duble_array[-1]):
                sent_mail_array.append(race_id)
                del race_result_inssert_data[race_id]
                fixed_flag=1
            check_array_count=check_array_count+1
            duble_array=[]

        if len(sent_mail_array)!=0:
            sent_mail(sent_mail_array)
        return race_result_inssert_data,fixed_flag

def make_csv(target_file_path,fixed_flag,make_csv_array):
    csv_outpath=target_file_path
    csv_outpath=csv_outpath.replace(".csv","")
    csv_outpath=csv_outpath+"_fixed.csv"

    #race_resultの場合
    if fixed_flag==1:
        header=["race_id","year,month","day","weekday","kai","nitime","race_number","race_name","place","course_distance","track","course_type","horseage_conditions","race_class","grade","weight_type","only_hinba","weather","turf_condition","dirt_condition",
            "start_race_time","entry","wakuban","umaban","horse_name","horse_id","sex","horse_age","horse_weight","horse_weight_increase","carried_weight","jockey","jockey_id","jockey_belong_area","belong_area",
            "trainer","trainer_id","trainer_belong_area","abnormal_code","rank","race_time","corner_1_rank","corner_2_rank","corner_3_rank","corner_4_rank","last_3_furlong_time","time_lag"]

    #horse_idの場合
    elif fixed_flag==2:
        pass

    #trainer_infoの場合
    elif fixed_flag==3:
        header=["trainer_id","trainer_name","trainer_belong_area","belong_updata","active","firist_run","last_run"]
    
    #jockey_infoの場合
    elif fixed_flag==4:
        pass
    
    make_csv_array.insert(0,header)
    with open(csv_outpath, mode="w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerows(make_csv_array)    
    return

def sent_mail(sent_mail_array):
    str_count=0
    str=""

    SCOPES = ['https://www.googleapis.com/auth/gmail.readonly','https://www.googleapis.com/auth/gmail.send']
    json_path=config.json_path
    token_path=config.token_path

    creds = None
    #過去にログイン済みなら認証情報を再利用する
    if os.path.exists(token_path):
        creds = Credentials.from_authorized_user_file(token_path, SCOPES)
    else: #初回ログイン時の処理
        flow = InstalledAppFlow.from_client_secrets_file(json_path, SCOPES)
        creds = flow.run_local_server(port=8080)
        with open(token_path, 'w') as token:
            token.write(creds.to_json())

    while len(sent_mail_array)>str_count:
        str_temp=sent_mail_array[str_count]
        str=str+str_temp+"\n"
        str_count=str_count+1

    service = build('gmail', 'v1', credentials=creds)
    sender = "aweqsenotice@gmail.com"
    to = "aweqsenotice@gmail.com"
    subject = "インサートcsv不備"
    send_text = (
        "競馬AI用のDBにインサートする項目に不備があります。\n"
        "目視で確認してCSVを修正後、手動でインサートしてください。\n"
        f"レースIDは \n {str} です。"
    )

    message = MIMEText(send_text, "plain", "utf-8")
    message["to"] = to
    message["from"] = sender
    message["subject"] = subject

    raw_message = base64.urlsafe_b64encode(message.as_bytes()).decode()
    message_body = {"raw": raw_message}

    try:
        response = service.users().messages().send(
            userId="me",
            body=message_body
        ).execute()

        print(f"メールの送信完了 race_id={str}")
        return response

    except Exception as e:
        print(f"メール送信失敗 race_id={str}")
        print(e)
        return None

def convert_form_dict_to_list(for_insert_and_make_csv_array):
    from_dict_to_converted_array=[]
    for e in for_insert_and_make_csv_array.values():
        from_dict_to_converted_array.extend(e)
    return from_dict_to_converted_array
    
def dump_mysql():
    subprocess.run(["ssh","root@192.168.1.108","bash","/root/mysql/script/make_dump_and_move"])

def make_dir():
    #公開鍵認証必須
    subprocess.run(["ssh","root@192.168.1.101","bash","/srv/dev-disk-by-uuid-29f4a620-dfe9-4cb3-8687-148b707af7e8/SSD/script/make_dir"])

def export_csv_to_fileserver():
    subprocess.run(["ssh","root@192.168.1.101","bash","/srv/dev-disk-by-uuid-29f4a620-dfe9-4cb3-8687-148b707af7e8/SSD/script/pull_csv_windows"])













main()