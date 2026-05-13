import csv

#初期変数定義
db_race_id_array=[]
snapshot_min=5
get_tine="00:00:00"
ticket_type=4
umaban3=0
min_odds=0
max_odds=0
min_odds_log=0
max_odds_log=0

#レースIDのmmdddを削除する処理
dir_csv="C:\\Users\\dev-w\\Desktop\\before5_umaren.csv"
with open (dir_csv,"r") as f:
    reader=csv.DictReader(f)
    for row in reader:
        race_id=row["レースID"]
        db_race_id=race_id[:4]+race_id[8:]
        db_race_id_array.append(db_race_id)
        
print(db_race_id_array)