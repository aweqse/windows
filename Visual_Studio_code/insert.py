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
from time import sleep

#-------20260605-------
#これからやること
#集約テーブルを作成してupdateするクエリを作り呼び出す処理を書く
#集約する対象はインサートするcsvか全部の2択にする。h
#⑥差分(2026年分)をダウンロードしてinsert.pyの一連の処理が正しく行われることの確認
#⑦全体を管理するサーバーとスクリプトを書いて自動的にVMの起動、スクリプトの実行と一連の処理がうまく行くか試す.
#⑧本番環境のVMを建てて環境構築をなるべく自動化(win-getとpowershellで）する
#⑩推論csvのスクリプトを完成させる

def main():
    today=date.today()
    now = today.strftime("%Y%m%d")
    print("時刻の取得完了")
    conn,cursor=connect_mysql()
    make_dir()
    insert_race_result_csv_dir=config.insert_race_result_csv_dir
    insert_odds_csv_dir=config.insert_odds_csv_dir
    insert_horse_csv_dir=config.insert_horse_csv_dir
    output_csv=config.output_csv
    race_result_filename_array=race_result_get_filename(insert_race_result_csv_dir)
    horse_array=horse_get_filename(insert_horse_csv_dir)
    odds_array=odds_get_filename(insert_odds_csv_dir)
    print("フォルダ内の初期のファイル名の取得完了")

    #mysqlのdump（バックアップを取得処理を追加する)
    print("mysqlのdump開始")
    dump_mysql()

    while len(race_result_filename_array)!=0:
        #csvファイルは一つしかない想定なので[0]固定
        target_file=race_result_filename_array[0]
        target_file_path=insert_race_result_csv_dir+"\\"+target_file
        move_filename=output_csv+"\\"+target_file
        print("データのチェックを開始")
        race_result_insert_data,fixed_flag,sent_mail_stop_array,sent_mail_double_array=check_data(target_file_path)
        print("辞書を配列に変換開始")
        from_dict_to_converted_array=convert_form_dict_to_list(race_result_insert_data)

        #修正したcsvを書き出す処理
        export_csv_path=target_file_path
        print("csvに書き出す処理開始") 
        export_csv_path=make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array)
        
        #修正項目の確認要請メールを送る処理
        if len(sent_mail_stop_array)!=0 or len(sent_mail_double_array)!=0:
            sent_mail(sent_mail_stop_array,sent_mail_double_array)
        print("race_resultのインサート開始")
        insert_race_result(from_dict_to_converted_array,conn,cursor)
        
        #データの集約
        print("データの集計を開始します。")
        trainer_race_day_dict,jockey_race_day_dict,horse_race_result_dict,trainer_race_result_dict,jockey_race_result_dict,horse_id_place_dict,horse_distance_group_dict,horse_course_distancerace_dict,horse_course_type_dict,horse_turn_direction_dict,turf_course_type_dict,turf_condition_dict,dirt_condition_dict,horse_place_distance_group_dict,horse_course_type_distance_group_dict,horse_place_course_type_dict,horse_id_array,trainer_id_array,jockey_id_array,horse_id_and_place_key_array,horse_distance_group_key_array,horse_course_distancerace_key_array,horse_course_type_key_array,horse_turn_direction_key_array,horse_turf_course_type_array,horse_turf_condition_key_array,horse_dirt_condition_key_array,horse_place_distance_group_key_array,horse_course_type_distance_group_key_array,horse_place_course_type_key_array=make_summary_dict(export_csv_path,cursor)
        target_array,insert_flag=horse_summary(horse_race_result_dict,horse_id_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=trainer_summary(trainer_race_day_dict,trainer_race_result_dict,trainer_id_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=jockey_summary(jockey_race_day_dict,jockey_race_result_dict,jockey_id_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_place_summary(horse_id_place_dict,horse_id_and_place_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_distance_group_summary(horse_distance_group_dict,horse_distance_group_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_course_distance_summary(horse_course_distancerace_dict,horse_course_distancerace_key_array) 
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_course_type_summary(horse_course_type_dict,horse_course_type_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_turn_direction_summary(horse_turn_direction_dict,horse_turn_direction_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_turn_course_type_summary(turf_course_type_dict,horse_turf_course_type_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_turn_condition_summary(turf_condition_dict,horse_turf_condition_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_dirt_condition_summary(dirt_condition_dict,horse_dirt_condition_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_place_distance_group_summary(horse_place_distance_group_dict,horse_place_distance_group_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_course_type_distance_group_summary(horse_course_type_distance_group_dict,horse_course_type_distance_group_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        target_array,insert_flag=horse_place_course_type_summary(horse_place_course_type_dict,horse_place_course_type_key_array)
        summary_insert(conn,cursor,target_array,insert_flag)
        print("データの集約とインサート完了")

        #この処理は必ず最後に置く
        fixed_flag=0
        move_file(target_file_path,move_filename)
        race_result_filename_array=race_result_get_filename(insert_race_result_csv_dir)
        
    while len(horse_array)!=0:
        #馬情報の処理
        target_file=horse_array[0]
        target_file_path=insert_horse_csv_dir+"\\"+target_file
        print("馬情報のインサート開始")
        insert_horse(target_file_path,conn,cursor,now)
        move_filename=output_csv+"\\raw_"+target_file
        move_file(target_file_path,move_filename)
        horse_array=horse_get_filename(insert_horse_csv_dir)

    while len(odds_array)!=0:
        #オッズ情報の処理
        target_file=odds_array[0]
        target_file_path=insert_odds_csv_dir+"\\"+target_file
        print("オッズ情報のインサート開始")
        insert_odds(target_file_path,target_file,conn,cursor)
        move_filename=output_csv+"\\"+target_file
        move_file(target_file_path,move_filename)
        odds_array=odds_get_filename(insert_odds_csv_dir)

    #trainer_infoの処理
    insert_array,update_array=make_trainer_data(cursor,now)
    if len(insert_array)!=0:
        fixed_flag=3
        from_dict_to_converted_array=insert_array.copy()
        print("csvの書き出し処理開始")
        target_file="trainer_insert_"+str(now)+".csv"
        make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array)
        print("trainer_infoのインサート処理開始")
        insert_trainer_info(insert_array,conn,cursor)

    if len(update_array)!=0:
        fixed_flag=3
        from_dict_to_converted_array=update_array.copy()
        print("csvの書き出し処理開始")
        target_file="trainer_update_"+str(now)+".csv"
        make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array)
        print("trainer_infoのアップデート処理開始")
        update_trainer_info(update_array,conn,cursor)

    #jockey_infoの処理
    insert_array,update_array=make_jockey_info(cursor,now)
    if len(insert_array)!=0:
        fixed_flag=4
        from_dict_to_converted_array=insert_array.copy()
        print("csvの書き出し処理開始")
        target_file="jockey_insert_"+str(now)+".csv"
        make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array)
        print("jockey_infoのインサート処理開始")
        insert_jockey_info(insert_array,conn,cursor)

    if len(update_array)!=0:
        fixed_flag=4
        from_dict_to_converted_array=update_array.copy()
        print("csvの書き出し処理開始")
        target_file="jockey_update_"+str(now)+".csv"
        make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array)
        print("jockey_infoのアップデート処理開始")
        update_jockey_info(update_array,conn,cursor)

    #すべてのcsvをファイルサーバーに移動する
    #windowsからファイルサーバーにおくる方式に変更する
    export_csv_to_fileserver()

    #googledriveにアプロードするスクリプトの記述
    print("クラウドにバックアップ開始")
    upload_cloud()
    print("クラウドにバックアップ終了")
    
    #windowsのローカルファイルを削除する
    delete_source_file()
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

def horse_get_filename(insert_horse_csv_dir):
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
    insert_query="insert into race_result values (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
    cursor.executemany(insert_query, for_insert_and_make_csv_array)
    conn.commit()
    print("レース結果をコミットしました。")

#馬情報の処理
def insert_horse(target_file_path,conn,cursor,now):
    insert_horse_array=[]
    update_horse_array=[]
    run_row_dict={}
    judge_data_dict={}
    jugde_dict_count=0
    run_row_dict_count=0

    #insertかupdateか判断するための配列を取得
    judge_query="select horse_id,sex,trainer_id,trainer_name,owner_name,owner_id,final_run_day,reject_jra_date,trainer_belong_area from horse_info;"
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
            last_update = now
            
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
                            str(judge_data_dict[horse_id]["trainer_belong_area"])]
                            
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
            update_query="update horse_info set sex=%s,trainer_name=%s,trainer_id=%s,owner_name=%s,owner_id=%s,final_run_day=%s,reject_jra_date=%s,belong_area=%s,last_update=%s where horse_info.horse_id=%s;"      
            cursor.executemany(update_query, update_horse_array)

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
        min_fuku_search_odds=str(umaban_count)+"複Lo"
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
            min_odds=float(row[min_fuku_search_odds])
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
        search_str="馬"+str(umaban1)+"-"+str(umaban2)
         
        odds=float(row[search_str])
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
        seach_count=seach_count+1
    return data
        
def move_file(target_file_path,move_filename):
    source_path=Path(target_file_path)
    dest_path=Path(move_filename)
    #転送先を〜csvフォルダにする
    if dest_path.exists():
        dest_path.unlink()
    shutil.move(source_path,dest_path)
    print("csvファイルの移動が完了しました")

def make_trainer_data(cursor,now):
    print("調教師の更新処理開始")
    trainer_info_dict={}
    race_result_dict={}
    insert_array=[]
    update_array=[]

    #race_resultから更新候補の一覧を取得する
    make_dict_query="select trainer_id,trainer_name,trainer_belong_area,min(year*10000+month*100+day) as first_run,max(year*10000+month*100+day) as last_run from race_result group by trainer_id,trainer_name,trainer_belong_area;"
    cursor.execute(make_dict_query)
    trainer_race_result_array = cursor.fetchall()

    #辞書を作成する
    if len(trainer_race_result_array)!=0:
        for row in trainer_race_result_array:
            race_result_dict[row["trainer_id"]]={"trainer_name":row["trainer_name"],"trainer_belong_area":row["trainer_belong_area"],"first_run":row["first_run"],"last_run":row["last_run"]}
        print("race_resultからの情報取得完了")

    #trainer_infoから更新候補と比較して差分があればupdate処理、noneならinsert処理に分岐する
    trainer_info_query="select * from trainer_info;"
    cursor.execute(trainer_info_query)
    trainer_info_array = cursor.fetchall()

    #activeは全部のカラムが対象なので辞書とは別に全部の要素を配列に入れて全配列チェックする
    if len(trainer_info_array)!=0:
        for row in trainer_info_array:
            #updateの可能性があるので全部の要素を辞書に入れる
            trainer_info_dict[row["trainer_id"]]={"trainer_name":row["trainer_name"],"trainer_belong_area":row["trainer_belong_area"],"belong_update":row["belong_update"],"first_run":row["first_run"],"active":row["active"],"last_run":row["last_run"]}
        print("trainer_infoからの情報取得完了")

    #辞書からトレーナーIDが存在しない場合noneを返す
    for trainer_id in race_result_dict.keys():
        value=trainer_info_dict.get(trainer_id,None)

        #trainer_infoのtraner_idが辞書が該当すれば比較してupdate処理、なければinsert処理
        #insert処理
        if value==None:
            trainer_name=race_result_dict[trainer_id]["trainer_name"]
            belong_area=race_result_dict[trainer_id]["trainer_belong_area"]
            belong_update=None
            active=1
            first_run=race_result_dict[trainer_id]["first_run"]
            last_run=race_result_dict[trainer_id]["last_run"]
            data=[trainer_id,trainer_name,belong_area,belong_update,active,first_run,last_run]
            #trainer_idで重複チェックする
            insert_array.append(data)
            continue
    
        #trainer_infoの配列をactiveとbelong_areaをCheckして当てはまればかきかえる。
        #書き換えたら必ず､差分がでるのでupdate.arrayrrに格納する
        #書き替えがない場合は比較して差分があればupdate.arraに格納する。
        #差分を比較する値のみ取り出す。activeのみtrainer_infoから抽出する
        active=trainer_info_dict[trainer_id]["active"]   
        belong_area=race_result_dict[trainer_id]["trainer_belong_area"]
        last_run=race_result_dict[trainer_id]["last_run"]
        belong_update=None
        update_flag=0

        #active_checkのチェック 
        if active!=0:
            if int(trainer_info_dict[trainer_id]["last_run"])+20000<int(now):
                active=0
                update_flag=1
            #belong_area_check       
            if trainer_info_dict[trainer_id]["trainer_belong_area"]!=race_result_dict[trainer_id]["trainer_belong_area"]:
                belong_update=now
                update_flag=1
            if trainer_info_dict[trainer_id]["last_run"]!=race_result_dict[trainer_id]["last_run"]:
                last_run=race_result_dict[trainer_id]["last_run"]
                update_flag=1

            if update_flag==1:
                data=[active,belong_area,belong_update,last_run,trainer_id]
                update_array.append(data)
                print("差分あり、update配列に格納")
            else:
                print("差分なし")
        else:
            print("更新対象外です")

    return insert_array,update_array

def make_jockey_info(cursor,now):
    print("騎手の更新処理開始")
    jockey_race_result_info_dict={}
    jockey_info_dict={}
    insert_array=[]
    update_array=[]

    #race_resultから更新候補を取り出す
    jockey_race_result_query="select jockey_id,jockey_name,jockey_belong_area,jockey_free,jockey_belong_trainer_id,jockey_belong_trainer_name,min(year*10000+month*100+day) as first_run,max(year*10000+month*100+day) as last_run from race_result group by jockey_id,jockey_name,jockey_belong_area,jockey_free,jockey_belong_trainer_id,jockey_belong_trainer_name;"
    cursor.execute(jockey_race_result_query)
    jockey_race_result_info = cursor.fetchall()

    #辞書を作成する
    if len(jockey_race_result_info)!=0:
        for row in jockey_race_result_info:
            jockey_race_result_info_dict[row["jockey_id"]]={"jockey_name":row["jockey_name"],"jockey_belong_area":row["jockey_belong_area"],"jockey_free":row["jockey_free"],"jockey_belong_trainer_id":row["jockey_belong_trainer_id"],"jockey_belong_trainer_name":row["jockey_belong_trainer_name"],"first_run":row["first_run"],"last_run":row["last_run"]}
        print("race_resultからの情報取得完了")

    jockey_info_query="select * from jockey_info;"
    cursor.execute(jockey_info_query)
    jockey_info_array = cursor.fetchall()

    if len(jockey_info_array)!=0:
        for row in jockey_info_array:
            jockey_info_dict[row["jockey_id"]]={"jockey_name":row["jockey_name"],"jockey_belong_area":row["jockey_belong_area"],"free":row["free"],"jockey_belong_trainer_id":row["jockey_belong_trainer_id"],"jockey_belong_trainer_name":row["jockey_belong_trainer_name"],"belong_update":row["belong_update"],"active":row["active"],"first_run":row["first_run"],"last_run":row["last_run"]}
            print("jockey_infoからの情報取得完了")
    
    #辞書から騎手IDが存在しない場合noneを返す
    for jockey_id in jockey_race_result_info_dict.keys():
        value=jockey_info_dict.get(jockey_id,None)
        #trainer_infoのtraner_idが辞書が該当すれば比較してupdate処理、なければinsert処理
        #insert処理
        if value==None:
            jockey_name=jockey_race_result_info_dict[jockey_id]["jockey_name"]
            jockey_belong_area=jockey_race_result_info_dict[jockey_id]["jockey_belong_area"]
            free=jockey_race_result_info_dict[jockey_id]["jockey_free"]
            jockey_belong_jockey_id=jockey_race_result_info_dict[jockey_id]["jockey_belong_trainer_id"]
            jockey_belong_trainer_name=jockey_race_result_info_dict[jockey_id]["jockey_belong_trainer_name"]
            jockey_belong_update=None
            active=1
            first_run=jockey_race_result_info_dict[jockey_id]["first_run"]
            last_run=jockey_race_result_info_dict[jockey_id]["last_run"]
            data=[jockey_id,jockey_name,jockey_belong_area,free,jockey_belong_jockey_id,jockey_belong_trainer_name,jockey_belong_update,active,first_run,last_run]
            #trainer_idで重複チェックする
            insert_array.append(data)
            continue
        #差分を比較するために値を抽出する。
        active=jockey_info_dict[jockey_id]["active"]
        belong_update=jockey_info_dict[jockey_id]["belong_update"]
        jockey_belong_area=jockey_race_result_info_dict[jockey_id]["jockey_belong_area"]
        free=jockey_race_result_info_dict[jockey_id]["jockey_free"]
        jockey_belong_jockey_id=jockey_race_result_info_dict[jockey_id]["jockey_belong_trainer_id"]
        jockey_belong_trainer_name=jockey_race_result_info_dict[jockey_id]["jockey_belong_trainer_name"]
        last_run=jockey_race_result_info_dict[jockey_id]["last_run"]
        update_flag=0

        if active!=0:
            #freeのチェック
            if free!=jockey_info_dict[jockey_id]["free"]:
                if free==0:
                    free=1
                    jockey_belong_trainer_name=None
                    update_flag=1 
                elif free==1:
                    free=0
                    update_flag=1
            if int(jockey_info_dict[jockey_id]["last_run"])+20000<int(now):
                active=0
                update_flag=1
            if jockey_belong_area!=jockey_info_dict[jockey_id]["jockey_belong_area"]:
                belong_update=now
                update_flag=1
            if last_run!=jockey_info_dict[jockey_id]["last_run"]:
                update_flag=1

            if update_flag==1:
                data=[jockey_belong_area,free,jockey_belong_jockey_id,jockey_belong_trainer_name,belong_update,active,last_run,jockey_id]
                update_array.append(data)
                print("差分あり、update配列に格納")
            else:
                print("差分なし")
        else:
            print("更新対象外です")

    return insert_array,update_array
        
def insert_trainer_info(insert_array,conn,cursor):
    #インサート処理
    insert_srl="insert into trainer_info(trainer_id,trainer_name,trainer_belong_area,belong_update,active,first_run,last_run) values(%s,%s,%s,%s,%s,%s,%s)"
    cursor.executemany(insert_srl, insert_array)
    conn.commit()
    print("インサート処理が完了しました")

def update_trainer_info(update_array,conn,cursor):
    #update処理
    update_query="update trainer_info set active=%s,trainer_belong_area=%s,belong_update=%s,last_run=%s where trainer_id=%s;"
    cursor.executemany(update_query, update_array)
    conn.commit()
    print("アップデート処理が完了しました。")

def insert_jockey_info(insert_array,conn,cursor):
    insert_query="insert into jockey_info values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
    cursor.executemany(insert_query, insert_array)
    conn.commit()
    print("インサート処理が完了しました")

def update_jockey_info(update_array,conn,cursor):
    update_query="update jockey_info set jockey_belong_area=%s,free=%s,jockey_belong_trainer_id=%s,jockey_belong_trainer_name=%s,belong_update=%s,active=%s,last_run=%s where jockey_id=%s;"
    cursor.executemany(update_query, update_array)
    conn.commit()
    print("アップデート処理が完了しました。")

def check_data(target_file_path):
    race_result_insert_data={}
    race_id_array=[]
    unique_key_array=[]
    check_array_count=0
    sent_mail_stop_array=[]
    sent_mail_double_array=[]
    fixed_flag=0
    
    #検査用の辞書とrace_idの配列を作成する
    print("検査用の辞書作成開始")
    TRACK_MAP = {10: (0, 0, None, 2, None),
                11: (0, 0, None, 1, None),
                12: (0, 0, None, 1, 1),
                13: (0, 0, None, 1, 2),
                14: (0, 0, None, 1, 3),
                15: (0, 0, None, 1, 4),
                16: (0, 0, None, 1, 5),
                17: (0, 0, None, 0, None),
                18: (0, 0, None, 0, 1),
                19: (0, 0, None, 0, 2),
                20: (0, 0, None, 0, 3),
                21: (0, 0, None, 0, 4),
                22: (0, 0, None, 0, 5),
                23: (0, 1, None, 1, None),
                24: (0, 1, None, 0, None),
                25: (0, 1, None, 1, 0),
                26: (0, 1, None, 0, 1),
                27: (0, 2, None, 1, None),
                28: (0, 2, None, 0, None),
                29: (0, 1, None, 2, None),
                51: (1, 0, 0, None, None),
                52: (1, 0, 1, None, None),
                53: (1, 0, None, 1, None),
                54: (1, 0, None, None, None),
                55: (1, 0, None, None, 1),
                56: (1, 0, None, None, 3),
                57: (1, 0, None, None, 2),
                58: (1, 0, None, None, 6),
                59: (1, 0, None, None, 7),}
    with open(target_file_path, mode="r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            #インサート用の辞書を作成t
            if 1000<=int(row["course_distance"])<=1300:
                distance_group=0
            elif 1400<=int(row["course_distance"])<=1600:
                distance_group=1
            elif 1700<=int(row["course_distance"])<=2200:
                distance_group=2
            elif 2300<=int(row["course_distance"])<=2600:
                distance_group=3
            elif 2700<=int(row["course_distance"]):
                distance_group=4
            else:
                distance_group=None
            
            (race_type,course_type_2,jump_course_type,turn_direction,turn_direction_details) = TRACK_MAP.get(int(row["track"]), (None, None, None, None, None))

            data=[row["race_id"],row["year"],row["month"],row["day"],row["weekday"],row["kai"],row["nitime"],row["race_number"],row["race_name"],row["place"],row["course_distance"],
                distance_group,row["track"],race_type,course_type_2,jump_course_type,turn_direction,turn_direction_details,
                row["course_type"],row["horseage_conditions"],row["race_class"],row["grade"],row["weight_type"],row["only_hinba"],row["weather"],row["turf_condition"],row["dirt_condition"],
                row["start_race_time"],row["entry"],row["wakuban"],row["umaban"],row["horse_id"],row["horse_name"],row["sex"],row["horse_age"],
                row["horse_weight"],row["horse_weight_increase"],row["carried_weight"],row["jockey_id"],row["jockey"],row["jockey_belong_area"],
                row["jockey_free"],row["jockey_belong_trainer_id"],row["jockey_belong_trainer"],
                row["trainer_id"],row["trainer"],row["trainer_belong_area"],row["abnormal_code"],row["rank"],row["ninki"],row["race_time"],
                row["corner_1_rank"],row["corner_2_rank"],row["corner_3_rank"],row["corner_4_rank"],row["last_3_furlong_time"],row["last_3_furlong_rank"],row["time_lag"]]

            #nullチェックをする
            print("nullチェック開始")
            data=convert_null(data)
            
            # race_idだけだと代替開催・取りやめで混ざるので年月日を足す
            print("拡張レースIDの取得開始")
            race_id = row["race_id"] + row["year"] + row["month"] + row["day"]
            if race_id not in race_result_insert_data:
                race_result_insert_data[race_id] = []
            race_result_insert_data[race_id].append(data)
            race_id_array.append(race_id)

            print("馬番重複用配列の取得開始")
            unique_key=row["race_id"]+row["umaban"]
            unique_key_array.append(unique_key)

        # race_id_arrayの重複を削除する
        race_id_array = list(set(race_id_array))

        #unique_key_arrayの重複を削除する
        unique_key_array = list(set(unique_key_array))

        # rankの値を検査する
        while len(race_id_array) > check_array_count:
            print("レース取りやめの検査開始")
            race_id = race_id_array[check_array_count]
            rank_cheak_array = race_result_insert_data[race_id]
            # rankがすべて0の場合、レース取りやめの可能性があるため配列を削除する
            if all(int(e[48]) == 0 for e in rank_cheak_array):
                del race_result_insert_data[race_id]
                # 通知には元のrace_idだけ入れる
                if rank_cheak_array[0][0] not in sent_mail_stop_array:
                    sent_mail_stop_array.append(rank_cheak_array[0][0])
                fixed_flag = 1
                check_array_count = check_array_count + 1
                print("レースの取り止めあり")
                continue

            print("馬番被りの検査開始")
            for r in rank_cheak_array:
                unir_value=str(r[0])+str(r[30])
                if unir_value in unique_key_array:
                    unique_key_array.remove(unir_value)
                else:
                    race_result_insert_data.pop(race_id, None)
                    if r[0] not in sent_mail_double_array:
                        sent_mail_double_array.append(r[0])
                    fixed_flag = 1
                    print("馬番被りあり:", r[0])

                    break
            check_array_count = check_array_count + 1

        return race_result_insert_data, fixed_flag, sent_mail_stop_array,sent_mail_double_array

def make_csv(target_file,output_csv,fixed_flag,from_dict_to_converted_array):
    #race_resultの場合
    if fixed_flag==0 or fixed_flag==1:  
        header=["race_id","year","month","day","weekday","kai","nitime","race_number","race_name","place","course_distance","distance_group","track","race_type","course_type","jump_course_type","turn_direction","turn_direction_details","turn_course_type","horseage_conditions",
            "race_class","grade","weight_type","only_hinba","weather","turf_condition","dirt_condition","start_race_time","entry","wakuban","umaban","horse_id",
            "horse_name","sex","horse_age","horse_weight","horse_weight_increase","carried_weight","jockey_id","jockey_name","jockey_belong_area","jockey_free","jockey_belong_trainer_id",
            "jockey_belong_trainer_name","trainer_id","trainer_name","trainer_belong_area","abnormal_code","rank","ninki","race_time",
            "corner_1_rank","corner_2_rank","corner_3_rank","corner_4_rank","last_3_furlong_time","last_3_furlong_rank","time_lag"]

        if fixed_flag==1:
            target_file=target_file.replace(".csv","")
            fixed_filename=target_file+"_fixed.csv"
            export_csv_path=output_csv+"\\"+fixed_filename
        else:
            export_csv_path=output_csv+"\\"+target_file
    #horse_idの場合
    elif fixed_flag==2:
        pass

    #trainer_infoの場合
    elif fixed_flag==3:
        header=["trainer_id","trainer_name","trainer_belong_area","belong_update","active","first_run","last_run"]
        export_csv_path=output_csv+"\\"+target_file
    
    #jockey_infoの場合
    elif fixed_flag==4:
        header=["jockey_id","jockey_name","jockey_belong_area","free","jockey_belong_trainer_id","jockey_belong_trainer_name","belong_update","active","first_run","last_run"]
        export_csv_path=output_csv+"\\"+target_file
    
    #csv書き出し処理
    export_csv_array=from_dict_to_converted_array.copy()
    export_csv_array.insert(0,header)
    with open(export_csv_path, mode="w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerows(export_csv_array)    
    return export_csv_path

def sent_mail(sent_mail_stop_array,sent_mail_double_array):
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
    if len(sent_mail_stop_array)!=0:
        while len(sent_mail_stop_array)>str_count:
            str_temp=sent_mail_stop_array[str_count]
            str=str+str_temp+"\n"
            str_count=str_count+1

        service = build('gmail', 'v1', credentials=creds)
        sender = "aweqrenotice@gmail.com"
        to = "aweqsenotice@gmail.com"
        subject = "インサートcsv不備"
        send_text = (
            "CSVにレース取りやめがあります。\n"
            "手動での対処は必要ないはずですが念のためmysqlで対象のレースIDを条件にrankを検索して0出ないことを確認してください。\n"
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
    else:
        while len(sent_mail_double_array)>str_count:
            str_temp=sent_mail_double_array[str_count]
            str=str+str_temp+"\n"
            str_count=str_count+1

        service = build('gmail', 'v1', credentials=creds)
        sender = "aweqsenotice@gmail.com"
        to = "aweqsenotice@gmail.com"
        subject = "インサートcsv不備"
        send_text = (
            "CSVに馬番の重複があります。\n"
            "記載されているレースIDは中途半端にmysqlに登録されている可能性があるのでレースIDで検索して必要ならば削除してください。その後、CSVを修正しインサート処理を実行してください\n"
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


def convert_form_dict_to_list(target_dict):
    from_dict_to_converted_array=[]
    for e in target_dict.values():
        from_dict_to_converted_array.extend(e)
    return from_dict_to_converted_array
    
def dump_mysql():
    subprocess.run(["ssh","root@192.168.1.108","bash","/root/mysql/script/make_dump_and_move"])

def make_dir():
    #公開鍵認証必須
    subprocess.run(["ssh","root@192.168.1.101","bash","/srv/dev-disk-by-uuid-29f4a620-dfe9-4cb3-8687-148b707af7e8/SSD/script/make_dir"])

def upload_cloud():
    subprocess.run(["ssh","root@192.168.1.101","bash","/srv/dev-disk-by-uuid-29f4a620-dfe9-4cb3-8687-148b707af7e8/SSD/script/upload_cloud"])

def export_csv_to_fileserver():
    code_path="C:\\Users\\dev-w\\Desktop\\workspace\\output\\script\\from_local_to_server.ps1"
    subprocess.run(["powershell","-ExecutionPolicy","Bypass","-File",code_path],check=True)

def delete_source_file():
    target_dir=Path(r"C:\\Users\\dev-w\\Desktop\\workspace\\output\\csv")
    for item in target_dir.iterdir():
        if item.is_dir():
            shutil.rmtree(item)
        else:
            item.unlink()

def make_summary_dict(export_csv_path,cursor):
    horse_race_result_dict={}
    trainer_summary_dict={}
    jockey_summary_dict={}
    horse_id_place_dict={}
    horse_distance_group_dict={}
    horse_course_distancerace_dict={}
    horse_course_type_dict={}
    horse_turn_direction_dict={}
    turf_course_type_dict={}
    turf_condition_dict={}
    dirt_condition_dict={}
    horse_place_distance_group_dict={}
    horse_course_type_distance_group_dict={}
    horse_place_course_type_dict={}
    trainer_race_day_dict={}
    jockey_race_day_dict={}
    horse_id_array=[]
    trainer_id_array=[]
    jockey_id_array=[]
    ymd_array=[]
    horse_id_and_place_key_array=[]
    horse_distance_group_key_array=[]
    horse_course_distancerace_key_array=[]
    horse_course_type_key_array=[]
    horse_turn_direction_key_array=[]
    horse_turf_course_type_array=[]
    horse_turf_condition_key_array=[]
    horse_dirt_condition_key_array=[]
    horse_place_distance_group_key_array=[]
    horse_course_type_distance_group_key_array=[]
    horse_place_course_type_key_array=[]
    jockey_summary_key_array=[]
    query_count=0
    
    #csvを読み込んで集約に必要な情報を取得する。一括の集約は作らず面倒でもcsv単位で集約する 
    with open(export_csv_path, mode="r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            year=row["year"]
            month=row["month"]
            day=row["day"]
            race_id=row["race_id"]
            trainer_id=row["trainer_id"]
            jockey_id=row["jockey_id"]
            umaban=row["umaban"]

            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            
            ymd=str(year)+str(month)+str(day)
            race_day_now=ymd
            if trainer_id not in trainer_race_day_dict:
                trainer_race_day_dict[trainer_id]=set()
            trainer_race_day_dict[trainer_id].add((race_day_now,race_id,umaban))

            if jockey_id not in jockey_race_day_dict:
                jockey_race_day_dict[jockey_id]=set()
            jockey_race_day_dict[jockey_id].add((race_day_now,race_id,umaban))

            ymd_array.append(int(ymd))
            horse_id_array.append(int(row["horse_id"]))
            trainer_id_array.append(int(trainer_id))
            jockey_id_array.append(int(row["jockey_id"]))

        horse_id_array=list(set(horse_id_array))
        trainer_id_array=list(set(trainer_id_array))
        jockey_id_array=list(set(jockey_id_array))
    
    print("クエリの作成開始")
    horse_query_str=trainer_query_str=jockey_query_str="("

    max_ymd=str(max(ymd_array))

    while len(horse_id_array)>=query_count:
        if len(horse_id_array)==query_count:
            horse_id=horse_id_array[query_count-1]
            horse_query_str=horse_query_str+"horse_id="+str(horse_id)+") and year*10000+month*100+day <="+max_ymd+";"
        elif len(horse_id_array)>query_count:
            horse_id=horse_id_array[query_count]
            horse_query_str=horse_query_str+"horse_id="+str(horse_id)+" or "
        query_count=query_count+1
    
    query_count=0
    while len(trainer_id_array)>=query_count:
        if len(trainer_id_array)==query_count:
            trainer_id=trainer_id_array[query_count-1]
            trainer_query_str=trainer_query_str+"trainer_id="+str(trainer_id)+") and year*10000+month*100+day <="+max_ymd+";"
        elif len(trainer_id_array)>query_count:
            trainer_id=trainer_id_array[query_count]
            trainer_query_str=trainer_query_str+"trainer_id="+str(trainer_id)+" or "
        query_count=query_count+1
    
    query_count=0
    while len(jockey_id_array)>=query_count:
        if len(jockey_id_array)==query_count:
            jockey_id=jockey_id_array[query_count-1]
            jockey_query_str=jockey_query_str+"jockey_id="+str(jockey_id)+") and year*10000+month*100+day <="+max_ymd+";"
        elif len(jockey_id_array)>query_count:
            jockey_id=jockey_id_array[query_count]
            jockey_query_str=jockey_query_str+"jockey_id="+str(jockey_id)+" or "
        query_count=query_count+1

    horse_summary_query="select race_id,horse_id,trainer_id,jockey_id,year,month,day,umaban,place,distance_group,course_distance,course_type,turn_direction,race_rank,turf_course_type,turf_condition,dirt_condition,race_time,race_ninki,last_3_furlong_time,last_3_furlong_rank,time_lag from race_result where "+horse_query_str 
    cursor.execute(horse_summary_query)
    horse_summary_result_array = cursor.fetchall()

    trainer_summary_query="select race_id,horse_id,trainer_id,jockey_id,year,month,day,umaban,race_rank,race_time,race_ninki,last_3_furlong_time,last_3_furlong_rank,time_lag from race_result where "+trainer_query_str 
    cursor.execute(trainer_summary_query)
    trainer_summary_result_array = cursor.fetchall()

    jockey_summary_query="select race_id,horse_id,trainer_id,jockey_id,year,month,day,umaban,race_rank,race_time,race_ninki,last_3_furlong_time,last_3_furlong_rank,time_lag from race_result where "+jockey_query_str 
    cursor.execute(jockey_summary_query)
    jockey_summary_result_array = cursor.fetchall()
    print("辞書の作成開始")

    for row in horse_summary_result_array:
        horse_id=row["horse_id"]
        place=row["place"]
        distance_group=row["distance_group"]    
        course_distancerace=row["course_distance"]
        course_type=row["course_type"]
        turn_direction=row["turn_direction"]
        turf_course_type=row["turf_course_type"]
        turf_condition=row["turf_condition"]
        dirt_condition=row["dirt_condition"]
        time_lag=row["time_lag"]

        data_1={"year":row["year"],"month":row["month"],"day":row["day"],"race_id":row["race_id"],"umaban":row["umaban"],
            "place":row["place"],"distance_group":row["distance_group"],"course_distance":row["course_distance"],"course_type":row["course_type"],"turn_direction":row["turn_direction"],"turf_course_type":row["turf_course_type"],"turf_condition":row["turf_condition"],"dirt_condition":row["dirt_condition"],
            "race_rank":row["race_rank"],"race_time":row["race_time"],"race_ninki":row["race_ninki"],"last_3_furlong_time":row["last_3_furlong_time"],"last_3_furlong_rank":row["last_3_furlong_rank"],"time_lag":row["time_lag"]}

        if horse_id not in horse_race_result_dict:
            horse_race_result_dict[horse_id] = []
        horse_race_result_dict[horse_id].append(data_1)

        horse_id_and_place_key=(horse_id,place)
        if horse_id_and_place_key not in horse_id_place_dict:
            horse_id_place_dict[horse_id_and_place_key]=[]
        horse_id_place_dict[horse_id_and_place_key].append(data_1)
        horse_id_and_place_key_array.append(horse_id_and_place_key)

        horse_distance_group_key=(horse_id,distance_group)
        if horse_distance_group_key not in horse_distance_group_dict:
            horse_distance_group_dict[horse_distance_group_key]=[]
        horse_distance_group_dict[horse_distance_group_key].append(data_1)
        horse_distance_group_key_array.append(horse_distance_group_key)

        horse_course_distance_key=(horse_id,course_distancerace)
        if horse_course_distance_key not in horse_course_distancerace_dict:
            horse_course_distancerace_dict[horse_course_distance_key]=[]
        horse_course_distancerace_dict[horse_course_distance_key].append(data_1)
        horse_course_distancerace_key_array.append(horse_course_distance_key)        

        horse_course_type_key=(horse_id,course_type)
        if horse_course_type_key not in horse_course_type_dict:
            horse_course_type_dict[horse_course_type_key]=[]
        horse_course_type_dict[horse_course_type_key].append(data_1)
        horse_course_type_key_array.append(horse_course_type_key)

        horse_turn_direction_key=(horse_id,turn_direction)
        if horse_turn_direction_key not in horse_turn_direction_dict:
            horse_turn_direction_dict[horse_turn_direction_key]=[]
        horse_turn_direction_dict[horse_turn_direction_key].append(data_1)
        horse_turn_direction_key_array.append(horse_turn_direction_key)

        horse_turf_course_type_key=(horse_id,turf_course_type)
        if horse_turf_course_type_key not in turf_course_type_dict:
            turf_course_type_dict[horse_turf_course_type_key]=[]
        turf_course_type_dict[horse_turf_course_type_key].append(data_1)
        horse_turf_course_type_array.append(horse_turf_course_type_key)

        horse_turf_condition_key=(horse_id,turf_condition)
        if horse_turf_condition_key not in turf_condition_dict:
            turf_condition_dict[horse_turf_condition_key]=[]
        turf_condition_dict[horse_turf_condition_key].append(data_1)
        horse_turf_condition_key_array.append(horse_turf_condition_key)

        horse_dirt_condition_key=(horse_id,dirt_condition)
        if horse_dirt_condition_key not in dirt_condition_dict:
            dirt_condition_dict[horse_dirt_condition_key]=[]
        dirt_condition_dict[horse_dirt_condition_key].append(data_1)
        horse_dirt_condition_key_array.append(horse_dirt_condition_key)

        horse_place_distance_group_key=(horse_id,place,distance_group)
        if horse_place_distance_group_key not in horse_place_distance_group_dict:
            horse_place_distance_group_dict[horse_place_distance_group_key]=[]
        horse_place_distance_group_dict[horse_place_distance_group_key].append(data_1)
        horse_place_distance_group_key_array.append(horse_place_distance_group_key)

        horse_course_type_distance_group_key=(horse_id,course_type,distance_group)
        if horse_course_type_distance_group_key not in horse_course_type_distance_group_dict:
            horse_course_type_distance_group_dict[horse_course_type_distance_group_key]=[]
        horse_course_type_distance_group_dict[horse_course_type_distance_group_key].append(data_1)
        horse_course_type_distance_group_key_array.append(horse_course_type_distance_group_key)

        horse_place_course_type_key=(horse_id,place,course_type)
        if horse_place_course_type_key not in horse_place_course_type_dict:
            horse_place_course_type_dict[horse_place_course_type_key]=[]
        horse_place_course_type_dict[horse_place_course_type_key].append(data_1)
        horse_place_course_type_key_array.append(horse_place_course_type_key)

    horse_id_and_place_key_array = list(dict.fromkeys(horse_id_and_place_key_array))
    horse_distance_group_key_array = list(dict.fromkeys(horse_distance_group_key_array))
    horse_course_distancerace_key_array = list(dict.fromkeys(horse_course_distancerace_key_array))
    horse_course_type_key_array = list(dict.fromkeys(horse_course_type_key_array))
    horse_turn_direction_key_array = list(dict.fromkeys(horse_turn_direction_key_array))
    horse_turf_course_type_array = list(dict.fromkeys(horse_turf_course_type_array))
    horse_turf_condition_key_array = list(dict.fromkeys(horse_turf_condition_key_array))
    horse_place_distance_group_key_array = list(dict.fromkeys(horse_place_distance_group_key_array))
    horse_dirt_condition_key_array = list(dict.fromkeys(horse_dirt_condition_key_array))
    horse_course_type_distance_group_key_array = list(dict.fromkeys(horse_course_type_distance_group_key_array))
    horse_place_course_type_key_array = list(dict.fromkeys(horse_place_course_type_key_array))

    for row in trainer_summary_result_array:
        trainer_id=row["trainer_id"]
        year=row["year"]
        month=row["month"]
        day=row["day"]

        if len(str(month))==1:
            month="0"+str(month)
        if len(str(day))==1:
            day="0"+str(day)
        trainer_race_day=str(year)+str(month)+str(day)       
        data_2={"trainer_race_day":trainer_race_day,"year":row["year"],"month":row["month"],"day":row["day"],"race_id":row["race_id"],"umaban":row["umaban"],"rank":row["race_rank"],
            "race_time":row["race_time"],"race_ninki":row["race_ninki"],"last_3_furlong_time":row["last_3_furlong_time"],"last_3_furlong_rank":row["last_3_furlong_rank"],"time_lag":row["time_lag"]}

        if trainer_id not in trainer_summary_dict:
            trainer_summary_dict[trainer_id]=[]
        trainer_summary_dict[trainer_id].append(data_2)
        
    for row in jockey_summary_result_array:
        jockey_id=row["jockey_id"]
        year=row["year"]
        month=row["month"]
        day=row["day"]

        if len(str(month))==1:
            month="0"+str(month)
        if len(str(day))==1:
            day="0"+str(day)
        jockey_race_day=str(year)+str(month)+str(day) 
        data_3={"jockey_race_day":jockey_race_day,"year":row["year"],"month":row["month"],"day":row["day"],"race_id":row["race_id"],"umaban":row["umaban"],"rank":row["race_rank"],
            "race_time":row["race_time"],"race_ninki":row["race_ninki"],"last_3_furlong_time":row["last_3_furlong_time"],"last_3_furlong_rank":row["last_3_furlong_rank"],"time_lag":row["time_lag"]}

        if jockey_id not in jockey_summary_dict:
            jockey_summary_dict[jockey_id]=[]
        jockey_summary_dict[jockey_id].append(data_3)
        jockey_summary_key_array.append(jockey_id)

    print("horse辞書の作成完了")

    return trainer_race_day_dict,jockey_race_day_dict,horse_race_result_dict,trainer_summary_dict,jockey_summary_dict,horse_id_place_dict,horse_distance_group_dict,horse_course_distancerace_dict,horse_course_type_dict,horse_turn_direction_dict,turf_course_type_dict,turf_condition_dict,dirt_condition_dict,horse_place_distance_group_dict,horse_course_type_distance_group_dict,horse_place_course_type_dict,horse_id_array,trainer_id_array,jockey_id_array,horse_id_and_place_key_array,horse_distance_group_key_array,horse_course_distancerace_key_array,horse_course_type_key_array,horse_turn_direction_key_array,horse_turf_course_type_array,horse_turf_condition_key_array,horse_dirt_condition_key_array,horse_place_distance_group_key_array,horse_course_type_distance_group_key_array,horse_place_course_type_key_array

def horse_summary(horse_race_result_dict,horse_id_array):
    print("集約の値の算出開始")
    summary_count=0
    horse_close_run_array=[]
    horse_summary_array=[]
    check_array=set()
    summary_key=() 

    while len(horse_id_array)>summary_count:
        print("horse_summary"+str(summary_count)+"/"+str(len(horse_id_array))+"の処理中です")
        horse_id=horse_id_array[summary_count]
        target_array_origin=horse_race_result_dict[horse_id]
        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        
        #配列の中身と要素を取得する。
        for target_index, r in enumerate(target_array_origin):
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
            race_id=r["race_id"]
            umaban=r["umaban"]
            year=r["year"]
            month=r["month"]
            day=r["day"]

            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]
            
            #テスト用パラメーター
            #horse_5run_race_count_array.append({ 'year':2018,'month':8,'day':26,'umaban':10,'rank':0,'race_time':1482,'last_3_furlong_time':392,'race_ninki':12})
            
            #rank0を取り除いて有効出走数を算出する
            horse_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            horse_close5_run_array=horse_close_run_array[:5].copy()
            horse_5run_race_count=len(horse_close5_run_array)
            horse_close10_run_array=horse_close_run_array[:10].copy()
            horse_1m_race_count=len(horse_close10_run_array)
    
            #6か月の有効出走数を集計する
            #6か月前の日付を算出する
            before_6month=int(month)-6
            if before_6month<=0:
                before_6year=int(year)-1
                before_6month=12+before_6month
            else:
                before_6year=year

            horse_close6month_run_array=horse_close_run_array.copy()
            under_day_1=before_6year*10000+before_6month*100+int(day)

            month6_array=[s for s in horse_close6month_run_array if int(s["year"])*10000+int(s["month"])*100+int(s["day"])>=under_day_1]
            horse_6m_race_count=len(month6_array)

            horse_close1year_run_array=horse_close6month_run_array.copy()
            before_12year=int(year)-1

            under_day_2=before_12year*10000+int(month)*100+int(day)
            month12_array=[s for s in horse_close1year_run_array if int(s["year"])*10000+int(s["month"])*100+int(s["day"])>=under_day_2]
            horse_12m_race_count=len(month12_array)

            #一着回数を算出する
            horse_close5_win1_array=[r for r in horse_close5_run_array if int(r["race_rank"])==1]
            horse_5run_win_count=len(horse_close5_win1_array)

            horse_close10_win1_array=[r for r in horse_close10_run_array if int(r["race_rank"])==1]
            horse_1m_win_count=len(horse_close10_win1_array)

            horse_close6month_win1_array=[r for r in month6_array if int(r["race_rank"])==1]
            horse_6m_win_count=len(horse_close6month_win1_array)
            
            horse_close1year_win1_array=[r for r in month12_array if int(r["race_rank"])==1]
            horse_12m_win_count=len(horse_close1year_win1_array)

            #連対回数を算出する
            horse_close5_rentai_array=[r for r in horse_close5_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2]
            horse_5run_top2_count=len(horse_close5_rentai_array)

            horse_close10_rentai_array=[r for r in horse_close10_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2]
            horse_1m_top2_count=len(horse_close10_rentai_array)

            horse_close6month_rentai_array=[r for r in month6_array if int(r["race_rank"])==1 or int(r["race_rank"])==2]
            horse_6m_top2_count=len(horse_close6month_rentai_array)
            
            horse_close1year_rentai_array=[r for r in month12_array if int(r["race_rank"])==1 or int(r["race_rank"])==2]
            horse_12m_top2_count=len(horse_close1year_rentai_array)

            #複勝を算出する
            horse_close5_fuku_array=[r for r in horse_close5_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3]
            horse_5run_top3_count=len(horse_close5_fuku_array)

            horse_close10_fuku_array=[r for r in horse_close10_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3]
            horse_1m_top3_count=len(horse_close10_fuku_array)

            horse_close6month_fuku_array=[r for r in month6_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3]
            horse_6m_top3_count=len(horse_close6month_fuku_array)
            
            horse_close1year_fuku_array=[r for r in month12_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3]
            horse_12m_top3_count=len(horse_close1year_fuku_array)

            #勝率を算出する
            target_count=horse_5run_win_count
            race_count=horse_5run_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_win_rate=cal_result

            target_count=horse_1m_win_count
            race_count=horse_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_win_rate=cal_result

            target_count=horse_6m_win_count
            race_count=horse_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_win_rate=cal_result
            
            target_count=horse_12m_win_count
            race_count=horse_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_win_rate=cal_result

            #連対率を算出する
            target_count=horse_5run_top2_count
            race_count=horse_5run_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_top2_rate=cal_result

            target_count=horse_1m_top2_count
            race_count=horse_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_top2_rate=cal_result

            target_count=horse_6m_top2_count
            race_count=horse_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_top2_rate=cal_result

            target_count=horse_12m_top2_count
            race_count=horse_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_top2_rate=cal_result

            #複勝率を算出する
            target_count=horse_5run_top3_count
            race_count=horse_5run_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_top3_rate=cal_result

            target_count=horse_1m_top3_count
            race_count=horse_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_top3_rate=cal_result

            target_count=horse_6m_top3_count
            race_count=horse_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_top3_rate=cal_result

            target_count=horse_12m_top3_count
            race_count=horse_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_top3_rate=cal_result

            #その他の項目を算出する
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in horse_close5_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank

            target_count=rank_sum
            race_count=horse_5run_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=horse_5run_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_5run_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in horse_close10_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank

            target_count=rank_sum
            race_count=horse_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=horse_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_1m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
            for e in month6_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank

            target_count=rank_sum
            race_count=horse_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=horse_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_6m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in month12_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank

            target_count=rank_sum
            race_count=horse_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=horse_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            horse_12m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            data=[race_id,umaban,summary_day, horse_id,
                horse_5run_race_count,horse_1m_race_count,horse_6m_race_count,horse_12m_race_count,
                horse_5run_win_count,horse_1m_win_count,horse_6m_win_count,horse_12m_win_count,
                horse_5run_top2_count,horse_1m_top2_count,horse_6m_top2_count,horse_12m_top2_count,
                horse_5run_top3_count,horse_1m_top3_count,horse_6m_top3_count,horse_12m_top3_count,
                horse_5run_win_rate,horse_1m_win_rate,horse_6m_win_rate,horse_12m_win_rate,
                horse_5run_top2_rate,horse_1m_top2_rate,horse_6m_top2_rate,horse_12m_top2_rate,
                horse_5run_top3_rate,horse_1m_top3_rate,horse_6m_top3_rate,horse_12m_top3_rate,
                horse_5run_avg_rank,horse_1m_avg_rank,horse_6m_avg_rank,horse_12m_avg_rank,
                horse_5run_avg_time_lag,horse_1m_avg_time_lag,horse_6m_avg_time_lag,horse_12m_avg_time_lag,
                horse_5run_avg_last_3f_rank,horse_1m_avg_last_3f_rank,horse_6m_avg_last_3f_rank,horse_12m_avg_last_3f_rank,
                horse_5run_avg_ninki,horse_1m_avg_ninki,horse_6m_avg_ninki,horse_12m_avg_ninki,           
                horse_5run_avg_rank_minus_ninki,horse_1m_avg_rank_minus_ninki,horse_6m_avg_rank_minus_ninki,horse_12m_avg_rank_minus_ninki,
                horse_5run_better_than_ninki_rate,horse_1m_better_than_ninki_rate,horse_6m_better_than_ninki_rate,horse_12m_better_than_ninki_rate]
            
            summary_key=(race_id,umaban)
            if summary_key in check_array:
                print("重複を除外")
                continue
            else:
                horse_summary_array.append(data)
                check_array.add(summary_key)

        summary_count=summary_count+1
    insert_flag=1
    print("馬の集計完了")
    return horse_summary_array,insert_flag
        
def trainer_summary(trainer_race_day_dict,trainer_race_result_dict,trainer_id_array):
    print("集約の値の算出開始")
    summary_count=0
    trainer_close_run_array=[]
    trainer_summary_array=[]
    check_array=set()
    summary_key=() 

    while len(trainer_id_array)>summary_count:
        print("trainer_summary"+str(summary_count)+"/"+str(len(trainer_id_array))+"の処理中です")
        trainer_id=trainer_id_array[summary_count]
        target_array_origin=trainer_race_result_dict[trainer_id]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["trainer_race_day"])),reverse=True)

        #race_dayを取り出して基準となるrace_dayを取り出す
        trainer_race_day_list=list(trainer_race_day_dict[str(trainer_id)])

        #rank0を取り除いて有効出走数を算出する
        trainer_close_run_array=[r for r in target_array_origin if int(r["rank"])!=0]

        #配列の中身と要素を取得する。
        for h ,race_id,umaban in trainer_race_day_list:
            summary_day=h
            #各日付を算出する
            under_day_0=int(h)-100
            under_day_1=int(h)-300
            under_day_2=int(h)-600
            under_day_3=int(h)-10000

            #出走期間と出走数を抽出する
            today_array=[s for s in trainer_close_run_array if int(s["trainer_race_day"])==summary_day]
            trainer_today_race_count=len(today_array)

            month1_array=[s for s in trainer_close_run_array if int(s["trainer_race_day"])>=under_day_0]
            trainer_1m_race_count=len(month1_array)

            month3_array=[s for s in trainer_close_run_array if int(s["trainer_race_day"])>=under_day_1]
            trainer_3m_race_count=len(month3_array)

            month6_array=[s for s in trainer_close_run_array if int(s["trainer_race_day"])>=under_day_2]
            trainer_6m_race_count=len(month6_array)

            month12_array=[s for s in trainer_close_run_array if int(s["trainer_race_day"])>=under_day_3]
            trainer_12m_race_count=len(month12_array)

            #一着回数を算出する
            trainer_today_win1_array=[r for r in today_array if int(r["rank"])==1]
            trainer_today_win_count=len(trainer_today_win1_array)

            trainer_close5_win1_array=[r for r in month1_array if int(r["rank"])==1]
            trainer_1m_win_count=len(trainer_close5_win1_array)

            trainer_close10_win1_array=[r for r in month3_array if int(r["rank"])==1]
            trainer_3m_win_count=len(trainer_close10_win1_array)

            trainer_close6month_win1_array=[r for r in month6_array if int(r["rank"])==1]
            trainer_6m_win_count=len(trainer_close6month_win1_array)
            
            trainer_close1year_win1_array=[r for r in month12_array if int(r["rank"])==1]
            trainer_12m_win_count=len(trainer_close1year_win1_array)

            #連対回数を算出する
            trainer_tooday_rentai_array=[r for r in today_array if int(r["rank"])==1 or int(r["rank"])==2]
            trainer_today_top2_count=len(trainer_tooday_rentai_array)
            
            trainer_1m_rentai_array=[r for r in month1_array if int(r["rank"])==1 or int(r["rank"])==2]
            trainer_1m_top2_count=len(trainer_1m_rentai_array)

            trainer_3m_rentai_array=[r for r in month3_array if int(r["rank"])==1 or int(r["rank"])==2]
            trainer_3m_top2_count=len(trainer_3m_rentai_array)

            trainer_6m_rentai_array=[r for r in month6_array if int(r["rank"])==1 or int(r["rank"])==2]
            trainer_6m_top2_count=len(trainer_6m_rentai_array)
            
            trainer_12m_rentai_array=[r for r in month12_array if int(r["rank"])==1 or int(r["rank"])==2]
            trainer_12m_top2_count=len(trainer_12m_rentai_array)

            #複勝を算出する
            trainer_tooday_rentai_array=[r for r in today_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            trainer_today_top3_count=len(trainer_tooday_rentai_array)

            trainer_close5_fuku_array=[r for r in month1_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            trainer_1m_top3_count=len(trainer_close5_fuku_array)

            trainer_close10_fuku_array=[r for r in month3_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            trainer_3m_top3_count=len(trainer_close10_fuku_array)

            trainer_close6month_fuku_array=[r for r in month6_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            trainer_6m_top3_count=len(trainer_close6month_fuku_array)
            
            trainer_close1year_fuku_array=[r for r in month12_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            trainer_12m_top3_count=len(trainer_close1year_fuku_array)

            #勝率を算出する
            target_count=trainer_today_win_count
            race_count=trainer_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_win_rate=cal_result

            target_count=trainer_1m_win_count
            race_count=trainer_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_win_rate=cal_result

            target_count=trainer_3m_win_count
            race_count=trainer_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_win_rate=cal_result

            target_count=trainer_6m_win_count
            race_count=trainer_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_win_rate=cal_result
            
            target_count=trainer_12m_win_count
            race_count=trainer_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_win_rate=cal_result

            #連対率を算出する
            target_count=trainer_today_top2_count
            race_count=trainer_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_top2_rate=cal_result

            target_count=trainer_1m_top2_count
            race_count=trainer_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_top2_rate=cal_result

            target_count=trainer_3m_top2_count
            race_count=trainer_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_top2_rate=cal_result

            target_count=trainer_6m_top2_count
            race_count=trainer_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_top2_rate=cal_result

            target_count=trainer_12m_top2_count
            race_count=trainer_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_top2_rate=cal_result

            #複勝率を算出する
            target_count=trainer_today_top3_count
            race_count=trainer_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_top3_rate=cal_result

            target_count=trainer_1m_top3_count
            race_count=trainer_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_top3_rate=cal_result

            target_count=trainer_3m_top3_count
            race_count=trainer_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_top3_rate=cal_result

            target_count=trainer_6m_top3_count
            race_count=trainer_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_top3_rate=cal_result

            target_count=trainer_12m_top3_count
            race_count=trainer_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_top3_rate=cal_result

            #その他の項目を算出する
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in today_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=trainer_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=trainer_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_today_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
           
            for e in month1_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=trainer_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=trainer_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_1m_better_than_ninki_rate=cal_result
            
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in month3_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=trainer_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=trainer_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_3m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in month6_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=trainer_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=trainer_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_6m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
            
            for e in month12_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=trainer_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_avg_time_lag=cal_result
            
            target_count=last_3_furlong_rank_sum
            race_count=trainer_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            trainer_12m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
        
            data=[race_id,umaban,summary_day,trainer_id,
                trainer_today_race_count,trainer_1m_race_count,trainer_3m_race_count,trainer_6m_race_count,trainer_12m_race_count,
                trainer_today_win_count,trainer_1m_win_count,trainer_3m_win_count,trainer_6m_win_count,trainer_12m_win_count,
                trainer_today_top2_count,trainer_1m_top2_count,trainer_3m_top2_count,trainer_6m_top2_count,trainer_12m_top2_count,
                trainer_today_top3_count,trainer_1m_top3_count,trainer_3m_top3_count,trainer_6m_top3_count,trainer_12m_top3_count,
                trainer_today_win_rate,trainer_1m_win_rate,trainer_3m_win_rate,trainer_6m_win_rate,trainer_12m_win_rate,
                trainer_today_top2_rate,trainer_1m_top2_rate,trainer_3m_top2_rate,trainer_6m_top2_rate,trainer_12m_top2_rate,
                trainer_today_top3_rate,trainer_1m_top3_rate,trainer_3m_top3_rate,trainer_6m_top3_rate,trainer_12m_top3_rate,
                trainer_today_avg_rank,trainer_1m_avg_rank,trainer_3m_avg_rank,trainer_6m_avg_rank,trainer_12m_avg_rank,
                trainer_today_avg_time_lag,trainer_1m_avg_time_lag,trainer_3m_avg_time_lag,trainer_6m_avg_time_lag,trainer_12m_avg_time_lag,
                trainer_today_avg_last_3f_rank,trainer_1m_avg_last_3f_rank,trainer_3m_avg_last_3f_rank,trainer_6m_avg_last_3f_rank,trainer_12m_avg_last_3f_rank,
                trainer_today_avg_ninki,trainer_1m_avg_ninki,trainer_3m_avg_ninki,trainer_6m_avg_ninki,trainer_12m_avg_ninki,           
                trainer_today_avg_rank_minus_ninki,trainer_1m_avg_rank_minus_ninki,trainer_3m_avg_rank_minus_ninki,trainer_6m_avg_rank_minus_ninki,trainer_12m_avg_rank_minus_ninki,
                trainer_today_better_than_ninki_rate,trainer_1m_better_than_ninki_rate,trainer_3m_better_than_ninki_rate,trainer_6m_better_than_ninki_rate,trainer_12m_better_than_ninki_rate]

            summary_key=(race_id,umaban)
            if summary_key in check_array:
                print("重複を除外")
                continue
            else:
                trainer_summary_array.append(data)
                check_array.add(summary_key)

        summary_count=summary_count+1

    print("調教師の集計完了")
    target_array=trainer_summary_array
    insert_flag=2
    return target_array,insert_flag

def jockey_summary(jockey_race_day_dict,jockey_race_result_dict,jockey_id_array):
    print("集約の値の算出開始")
    summary_count=0
    jockey_close_run_array=[]
    jockey_summary_array=[]
    check_array=set()
    summary_key=() 

    while len(jockey_id_array)>summary_count:
        print("jockey_summary"+str(summary_count)+"/"+str(len(jockey_id_array))+"の処理中です")
        jockey_id=jockey_id_array[summary_count]
        target_array_origin=jockey_race_result_dict[jockey_id]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["jockey_race_day"])),reverse=True)

        #race_dayを取り出して基準となるrace_dayを取り出す
        jockey_race_day_list=list(jockey_race_day_dict[str(jockey_id)])

        #rank0を取り除いて有効出走数を算出する
        jockey_close_run_array=[r for r in target_array_origin if int(r["rank"])!=0]

        #配列の中身と要素を取得する。
        for h ,race_id,umaban in jockey_race_day_list:
            summary_day=h
            #各日付を算出する
            under_day_0=int(h)-100
            under_day_1=int(h)-300
            under_day_2=int(h)-600
            under_day_3=int(h)-10000

            #出走期間と出走数を抽出する
            today_array=[s for s in jockey_close_run_array if int(s["jockey_race_day"])==summary_day]
            jockey_today_race_count=len(today_array)

            month1_array=[s for s in jockey_close_run_array if int(s["jockey_race_day"])>=under_day_0]
            jockey_1m_race_count=len(month1_array)

            month3_array=[s for s in jockey_close_run_array if int(s["jockey_race_day"])>=under_day_1]
            jockey_3m_race_count=len(month3_array)

            month6_array=[s for s in jockey_close_run_array if int(s["jockey_race_day"])>=under_day_2]
            jockey_6m_race_count=len(month6_array)

            month12_array=[s for s in jockey_close_run_array if int(s["jockey_race_day"])>=under_day_3]
            jockey_12m_race_count=len(month12_array)

            #一着回数を算出する
            jockey_today_win1_array=[r for r in today_array if int(r["rank"])==1]
            jockey_today_win_count=len(jockey_today_win1_array)

            jockey_close5_win1_array=[r for r in month1_array if int(r["rank"])==1]
            jockey_1m_win_count=len(jockey_close5_win1_array)

            jockey_close10_win1_array=[r for r in month3_array if int(r["rank"])==1]
            jockey_3m_win_count=len(jockey_close10_win1_array)

            jockey_close6month_win1_array=[r for r in month6_array if int(r["rank"])==1]
            jockey_6m_win_count=len(jockey_close6month_win1_array)
            
            jockey_close1year_win1_array=[r for r in month12_array if int(r["rank"])==1]
            jockey_12m_win_count=len(jockey_close1year_win1_array)

            #連対回数を算出する
            jockey_tooday_rentai_array=[r for r in today_array if int(r["rank"])==1 or int(r["rank"])==2]
            jockey_today_top2_count=len(jockey_tooday_rentai_array)
            
            jockey_1m_rentai_array=[r for r in month1_array if int(r["rank"])==1 or int(r["rank"])==2]
            jockey_1m_top2_count=len(jockey_1m_rentai_array)

            jockey_3m_rentai_array=[r for r in month3_array if int(r["rank"])==1 or int(r["rank"])==2]
            jockey_3m_top2_count=len(jockey_3m_rentai_array)

            jockey_6m_rentai_array=[r for r in month6_array if int(r["rank"])==1 or int(r["rank"])==2]
            jockey_6m_top2_count=len(jockey_6m_rentai_array)
            
            jockey_12m_rentai_array=[r for r in month12_array if int(r["rank"])==1 or int(r["rank"])==2]
            jockey_12m_top2_count=len(jockey_12m_rentai_array)

            #複勝を算出する
            jockey_tooday_rentai_array=[r for r in today_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            jockey_today_top3_count=len(jockey_tooday_rentai_array)

            jockey_close5_fuku_array=[r for r in month1_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            jockey_1m_top3_count=len(jockey_close5_fuku_array)

            jockey_close10_fuku_array=[r for r in month3_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            jockey_3m_top3_count=len(jockey_close10_fuku_array)

            jockey_close6month_fuku_array=[r for r in month6_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            jockey_6m_top3_count=len(jockey_close6month_fuku_array)
            
            jockey_close1year_fuku_array=[r for r in month12_array if int(r["rank"])==1 or int(r["rank"])==2 or int(r["rank"])==3]
            jockey_12m_top3_count=len(jockey_close1year_fuku_array)

            #勝率を算出する
            target_count=jockey_today_win_count
            race_count=jockey_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_win_rate=cal_result

            target_count=jockey_1m_win_count
            race_count=jockey_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_win_rate=cal_result

            target_count=jockey_3m_win_count
            race_count=jockey_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_win_rate=cal_result

            target_count=jockey_6m_win_count
            race_count=jockey_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_win_rate=cal_result
            
            target_count=jockey_12m_win_count
            race_count=jockey_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_win_rate=cal_result

            #連対率を算出する
            target_count=jockey_today_top2_count
            race_count=jockey_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_top2_rate=cal_result

            target_count=jockey_1m_top2_count
            race_count=jockey_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_top2_rate=cal_result

            target_count=jockey_3m_top2_count
            race_count=jockey_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_top2_rate=cal_result

            target_count=jockey_6m_top2_count
            race_count=jockey_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_top2_rate=cal_result

            target_count=jockey_12m_top2_count
            race_count=jockey_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_top2_rate=cal_result

            #複勝率を算出する
            target_count=jockey_today_top3_count
            race_count=jockey_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_top3_rate=cal_result

            target_count=jockey_1m_top3_count
            race_count=jockey_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_top3_rate=cal_result

            target_count=jockey_3m_top3_count
            race_count=jockey_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_top3_rate=cal_result

            target_count=jockey_6m_top3_count
            race_count=jockey_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_top3_rate=cal_result

            target_count=jockey_12m_top3_count
            race_count=jockey_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_top3_rate=cal_result

            #その他の項目を算出する
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in today_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=jockey_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=jockey_today_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_today_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
           
            for e in month1_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=jockey_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=jockey_1m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_1m_better_than_ninki_rate=cal_result
            
            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in month3_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=jockey_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=jockey_3m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_3m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0

            for e in month6_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=jockey_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=jockey_6m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_6m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
            
            for e in month12_array:
                rank=int(e["rank"])
                time_lag=int(e["time_lag"])
                ninki=int(e["race_ninki"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                    time_lag_count=time_lag_count+1
                else:
                    pass
                if ninki>0:
                    ninki_sum=ninki_sum+ninki
                    rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                    ninki_race_count=ninki_race_count+1
                    if rank<ninki:
                        over_rank_count=over_rank_count+1
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                
            target_count=rank_sum
            race_count=jockey_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_avg_rank=cal_result

            target_count=time_lag_sum
            race_count=time_lag_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_avg_time_lag=cal_result

            target_count=last_3_furlong_rank_sum
            race_count=jockey_12m_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_avg_last_3f_rank=cal_result

            target_count=ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_avg_ninki=cal_result

            target_count=rank_ninki_sum
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            race_count=ninki_race_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            jockey_12m_better_than_ninki_rate=cal_result

            rank=rank_sum=time_lag=time_lag_sum=ninki=ninki_sum=rank_ninki_sum=over_rank_count=last_3_furlong_rank_sum=ninki_race_count=time_lag_count=0
        
            data=[race_id,umaban,summary_day, jockey_id,
                jockey_today_race_count,jockey_1m_race_count,jockey_3m_race_count,jockey_6m_race_count,jockey_12m_race_count,
                jockey_today_win_count,jockey_1m_win_count,jockey_3m_win_count,jockey_6m_win_count,jockey_12m_win_count,
                jockey_today_top2_count,jockey_1m_top2_count,jockey_3m_top2_count,jockey_6m_top2_count,jockey_12m_top2_count,
                jockey_today_top3_count,jockey_1m_top3_count,jockey_3m_top3_count,jockey_6m_top3_count,jockey_12m_top3_count,
                jockey_today_win_rate,jockey_1m_win_rate,jockey_3m_win_rate,jockey_6m_win_rate,jockey_12m_win_rate,
                jockey_today_top2_rate,jockey_1m_top2_rate,jockey_3m_top2_rate,jockey_6m_top2_rate,jockey_12m_top2_rate,
                jockey_today_top3_rate,jockey_1m_top3_rate,jockey_3m_top3_rate,jockey_6m_top3_rate,jockey_12m_top3_rate,
                jockey_today_avg_rank,jockey_1m_avg_rank,jockey_3m_avg_rank,jockey_6m_avg_rank,jockey_12m_avg_rank,
                jockey_today_avg_time_lag,jockey_1m_avg_time_lag,jockey_3m_avg_time_lag,jockey_6m_avg_time_lag,jockey_12m_avg_time_lag,
                jockey_today_avg_last_3f_rank,jockey_1m_avg_last_3f_rank,jockey_3m_avg_last_3f_rank,jockey_6m_avg_last_3f_rank,jockey_12m_avg_last_3f_rank,
                jockey_today_avg_ninki,jockey_1m_avg_ninki,jockey_3m_avg_ninki,jockey_6m_avg_ninki,jockey_12m_avg_ninki,           
                jockey_today_avg_rank_minus_ninki,jockey_1m_avg_rank_minus_ninki,jockey_3m_avg_rank_minus_ninki,jockey_6m_avg_rank_minus_ninki,jockey_12m_avg_rank_minus_ninki,
                jockey_today_better_than_ninki_rate,jockey_1m_better_than_ninki_rate,jockey_3m_better_than_ninki_rate,jockey_6m_better_than_ninki_rate,jockey_12m_better_than_ninki_rate]
            
            summary_key=(race_id,umaban)
            if summary_key in check_array:
                print("重複を除外")
                continue
            else:
                jockey_summary_array.append(data)
                check_array.add(summary_key)

        summary_count=summary_count+1

    print("調教師の集計完了")
    target_array=jockey_summary_array
    insert_flag=3
    return target_array,insert_flag

def horse_place_summary(horse_id_place_dict,horse_id_and_place_key_array):
    summary_count=0
    horse_place_summary_array=[]
    while len(horse_id_and_place_key_array)>summary_count:
        key=horse_id_and_place_key_array[summary_count]
        target_array_origin=horse_id_place_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            place=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0
            
            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,place,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                  avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_place_summary_array.append(data)

        summary_count=summary_count+1

    insert_flag=4
    target_array=horse_place_summary_array
    print("馬と競馬場の集計完了")
    return target_array,insert_flag

def horse_distance_group_summary(horse_distance_group_dict,horse_distance_group_key_array):
    summary_count=0
    horse_distance_group_array=[]
    while len(horse_distance_group_key_array)>summary_count:
        key=horse_distance_group_key_array[summary_count]
        target_array_origin=horse_distance_group_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            distance_group=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,distance_group,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_distance_group_array.append(data)

        summary_count=summary_count+1

    insert_flag=5
    target_array=horse_distance_group_array
    print("馬と距離グループの集計完了")
    return target_array,insert_flag

def horse_course_distance_summary(horse_course_distancerace_dict,horse_course_distancerace_key_array):
    summary_count=0
    horse_course_distance_array=[]
    while len(horse_course_distancerace_key_array)>summary_count:
        key=horse_course_distancerace_key_array[summary_count]
        target_array_origin=horse_course_distancerace_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            course_distance=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,course_distance,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_course_distance_array.append(data)

        summary_count=summary_count+1

    insert_flag=6
    target_array=horse_course_distance_array
    print("馬と距離の集計完了")
    return target_array,insert_flag

def horse_course_type_summary(horse_course_type_dict,horse_course_type_key_array):
    summary_count=0
    horse_course_type_array=[]
    while len(horse_course_type_key_array)>summary_count:
        key=horse_course_type_key_array[summary_count]
        target_array_origin=horse_course_type_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            course_type=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,course_type,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_course_type_array.append(data)

        summary_count=summary_count+1

    insert_flag=7
    target_array=horse_course_type_array
    print("馬とコースタイプの集計完了")
    return target_array,insert_flag

def horse_turn_direction_summary(horse_turn_direction_dict,horse_turn_direction_key_array):
    summary_count=0
    horse_turn_direction_array=[]
    while len(horse_turn_direction_key_array)>summary_count:
        key=horse_turn_direction_key_array[summary_count]
        target_array_origin=horse_turn_direction_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            turn_direction=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,turn_direction,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_turn_direction_array.append(data)

        summary_count=summary_count+1

    insert_flag=8
    target_array=horse_turn_direction_array
    print("馬と周回方向の集計完了")
    return target_array,insert_flag

def horse_turn_course_type_summary(turf_course_type_dict,horse_turf_course_type_array):
    summary_count=0
    horse_turn_course_type_array=[]
    while len(horse_turf_course_type_array)>summary_count:
        key=horse_turf_course_type_array[summary_count]
        target_array_origin=turf_course_type_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            turn_course_type=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0
            
            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,turn_course_type,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_turn_course_type_array.append(data)

        summary_count=summary_count+1

    insert_flag=9
    target_array=horse_turn_course_type_array
    print("馬とコースタイプの集計完了")
    return target_array,insert_flag

def horse_turn_condition_summary(turf_condition_dict,horse_turf_condition_key_array):
    summary_count=0
    horse_turn_condition_array=[]
    while len(horse_turf_condition_key_array)>summary_count:
        key=horse_turf_condition_key_array[summary_count]
        target_array_origin=turf_condition_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            turf_condition=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0
            
            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,turf_condition,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]

            if turf_condition!=None:
                horse_turn_condition_array.append(data)

        summary_count=summary_count+1

    insert_flag=10
    target_array=horse_turn_condition_array
    print("馬と芝の状態の集計完了")
    return target_array,insert_flag

def horse_dirt_condition_summary(dirt_condition_dict,horse_dirt_condition_key_array):
    summary_count=0
    horse_dirt_condition_array=[]
    while len(horse_dirt_condition_key_array)>summary_count:
        key=horse_dirt_condition_key_array[summary_count]
        target_array_origin=dirt_condition_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            dirt_condition=key[1]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,dirt_condition,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]

            if dirt_condition!=None:
                horse_dirt_condition_array.append(data)

        summary_count=summary_count+1

    insert_flag=11
    target_array=horse_dirt_condition_array
    print("馬とダート状態の集計完了")
    return target_array,insert_flag

def horse_place_distance_group_summary(horse_place_distance_group_dict,horse_place_distance_group_key_array):
    summary_count=0
    horse_place_distance_group=[]
    while len(horse_place_distance_group_key_array)>summary_count:
        key=horse_place_distance_group_key_array[summary_count]
        target_array_origin=horse_place_distance_group_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            place=key[1]
            distance_group=key[2]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,place,distance_group,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_place_distance_group.append(data)

        summary_count=summary_count+1

    insert_flag=12
    target_array=horse_place_distance_group
    print("馬と競馬場と距離グループの集計完了")
    return target_array,insert_flag

def horse_course_type_distance_group_summary(horse_course_type_distance_group_dict,horse_course_type_distance_group_key_array):
    summary_count=0
    horse_course_type_distance_group=[]
    while len(horse_course_type_distance_group_key_array)>summary_count:
        key=horse_course_type_distance_group_key_array[summary_count]
        target_array_origin=horse_course_type_distance_group_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            course_type=key[1]
            distance_group=key[2]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,course_type,distance_group,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_course_type_distance_group.append(data)

        summary_count=summary_count+1

    insert_flag=13
    target_array=horse_course_type_distance_group
    print("馬とコースタイプと距離グループの集計完了")
    return target_array,insert_flag

def horse_place_course_type_summary(horse_place_course_type_dict,horse_place_course_type_key_array):
    summary_count=0
    horse_place_course_type=[]
    while len(horse_place_course_type_key_array)>summary_count:
        key=horse_place_course_type_key_array[summary_count]
        target_array_origin=horse_place_course_type_dict[key]

        #処理しやすいように降順でソートする
        target_array_origin.sort(key=lambda x: (int(x["year"]),int(x["month"]),int(x["day"])),reverse=True)
        for target_index, r in enumerate(target_array_origin):
            horse_id=key[0]
            place=key[1]
            course_type=key[2]
            year=r["year"]
            month=r["month"]
            day=r["day"]
            if len(str(month))==1:
                month="0"+str(month)
            if len(str(day))==1:
                day="0"+str(day)
            summary_day=str(year)+str(month)+str(day)

            #未来の日付を集計でとらないように要素を+1する。(ソートしてるので+1でOK)
            target_array = target_array_origin[target_index :]

            #競走中止を取り除く
            horse_place_close_run_array=[r for r in target_array if int(r["race_rank"])!=0]
            race_count=len(horse_place_close_run_array)
            win_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1])
            top2_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2])
            top3_count=len([r for r in horse_place_close_run_array if int(r["race_rank"])==1 or int(r["race_rank"])==2 or int(r["race_rank"])==3])
            
            target_count=win_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            win_rate=cal_result
            
            target_count=top2_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top2_rate=cal_result

            target_count=top3_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            top3_rate=cal_result

            rank_sum=ninki_sum=time_lag_sum=over_rank_count=rank_ninki_sum=last_3_furlong_rank_sum=0

            for e in horse_place_close_run_array:
                rank=int(e["race_rank"])
                time_lag=int(e["time_lag"])
                if time_lag!=9999:
                    time_lag_sum=time_lag_sum+time_lag
                else:
                    pass
                ninki=int(e["race_ninki"])
                last_3_furlong_rank=int(e["last_3_furlong_rank"])
                rank_sum=rank+rank_sum
                
                ninki_sum=ninki_sum+ninki
                last_3_furlong_rank_sum=last_3_furlong_rank_sum+last_3_furlong_rank
                rank_ninki_sum=(rank-ninki)+rank_ninki_sum
                if rank<ninki:
                    over_rank_count=over_rank_count+1

            target_count=rank_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank=cal_result

            target_count=time_lag_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_time_lag=cal_result

            target_count=ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_ninki=cal_result

            target_count=rank_ninki_sum
            cal_result=cal_rate_and_ave(target_count,race_count)
            avg_rank_minus_ninki=cal_result

            target_count=over_rank_count
            cal_result=cal_rate_and_ave(target_count,race_count)
            better_than_ninki_rate=cal_result

            data=[horse_id,summary_day,place,course_type,race_count,win_count,top2_count,top3_count,win_rate,top2_rate,top3_rate,
                    avg_rank,avg_time_lag,avg_ninki,avg_rank_minus_ninki,better_than_ninki_rate]
            horse_place_course_type.append(data)

        summary_count=summary_count+1

    insert_flag=14
    target_array=horse_place_course_type
    print("馬と競馬場とコースタイプの集計完了")
    return target_array,insert_flag

def cal_rate_and_ave(target_count,race_count):
    if race_count==0:
        cal_result=0
        return cal_result
    else:
        cal_result=target_count/race_count
        return cal_result

def summary_insert(conn,cursor,target_array,insert_flag):
    #インサート処理
    if insert_flag==1:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]
            
    elif insert_flag==2:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into trainer_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==3:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into jockey_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==4:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_place_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==5:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_distance_group_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==6:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_course_distance_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==7:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_course_type_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==8:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_turn_direction_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==9:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_turf_course_type_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==10:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_turf_condition_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==11:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_dirt_condition_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==12:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_place_distance_group_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==13:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_course_type_distance_group_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    elif insert_flag==14:
        while len(target_array)>0:
            insert_target=target_array[:1000]
            insert_srl="insert into horse_place_course_type_summary values(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)"
            cursor.executemany(insert_srl, insert_target)
            conn.commit()
            del target_array[:len(insert_target)]

    print("インサート処理が完了しました")

main()