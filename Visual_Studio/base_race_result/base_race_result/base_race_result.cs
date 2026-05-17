using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static JVData_Struct;


class Program
{
    // =========================
    // 基本設定
    // =========================

    // 通常運用では「先週の月曜〜日曜」のレース結果だけを取得する。
    // 手動で範囲指定したい場合は、Main内の「手動範囲指定」2行のコメントアウトを外す。
    //
    // 2 = 通常データ取得。週次差分・直近取得向け。
    // 過去年分などセットアップデータを取り直す場合は 4 に変更する。
    static readonly int OpenOption = 4;

    // 出力先
    static readonly string OutputDir = @"C:\Users\dev-w\Desktop\workspace\output\race_result_csv";
    static readonly string OutputFileNameBase = "race_result";

    // JVGetsバッファ
    static readonly int BUFSIZE = 110000;

    // rc=-3 ダウンロード中の待機設定
    static readonly int DownloadWaitMs = 5000;

    // 最大待機回数：5秒 × 240回 = 約20分
    static readonly int MaxDownloadWaitCount = 240;

    // SEHException発生時の最大リトライ回数
    static readonly int MaxSehRetryCount = 10;

    // JVOpen後、dl > 0 の場合に最初に待つ時間
    static readonly int InitialDownloadWaitMs = 30000;

    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 通常運用：
        // 今日の日付を基準にして「先週の月曜〜日曜」を自動取得する。
        // 例:
        //   今日が 2026-05-16(土) → 2026-05-04(月)〜2026-05-10(日)
        //   今日が 2026-05-18(月) → 2026-05-11(月)〜2026-05-17(日)
        DateTime startDate = GetLastWeekMonday(DateTime.Today);
        DateTime endDate = startDate.AddDays(6);

        string startDateText = ToYmd(startDate);
        string endDateText = ToYmd(endDate);

        // =========================
        // 手動範囲指定
        // =========================
        // 範囲を直接指定したい場合は、下の2行のコメントアウトを外して日付を変更する。
        // 例: 2024年の1年分を取得する場合
        //
        // startDateText = "20240101";
        // endDateText = "20241231";

        // exe引数で開始日・終了日を渡した場合は、引数を最優先する。
        // 例:
        //   base_race_result.exe 20240101 20241231
        if (args.Length >= 2)
        {
            startDateText = args[0];
            endDateText = args[1];
        }

        if (!Is8Digits(startDateText) || !Is8Digits(endDateText))
        {
            Console.WriteLine("日付指定が不正です。");
            Console.WriteLine("例: base_race_result.exe 20180101 20181231");
            Console.ReadLine();
            return;
        }

        if (!TryParseYmd(startDateText, out startDate) || !TryParseYmd(endDateText, out endDate))
        {
            Console.WriteLine("存在しない日付が指定されています。");
            Console.WriteLine("例: base_race_result.exe 20180101 20181231");
            Console.ReadLine();
            return;
        }

        if (endDate < startDate)
        {
            Console.WriteLine("終了日が開始日より前です。");
            Console.ReadLine();
            return;
        }

        string raceFromTime = startDateText + "000000";

        Directory.CreateDirectory(OutputDir);
        string outCsv = Path.Combine(OutputDir, OutputFileNameBase + "_" + startDateText + "_" + endDateText + ".csv");

        Console.WriteLine("CurrentDirectory=" + Environment.CurrentDirectory);
        Console.WriteLine("CSV path=" + outCsv);
        Console.WriteLine("取得開始日=" + startDate.ToString("yyyy-MM-dd"));
        Console.WriteLine("取得終了日=" + endDate.ToString("yyyy-MM-dd"));
        Console.WriteLine("取得モード=先週分自動取得。手動範囲指定またはexe引数がある場合は指定範囲を使用。");

        if (!Is14Digits(raceFromTime))
        {
            Console.WriteLine("raceFromTime が不正です: " + raceFromTime);
            Console.ReadLine();
            return;
        }

        var t = Type.GetTypeFromProgID("JVDTLab.JVLink");
        if (t == null)
        {
            Console.WriteLine("JV-Link(COM) が見つかりません。");
            Console.ReadLine();
            return;
        }

        dynamic jv = Activator.CreateInstance(t);

        int rc = jv.JVInit("RaceResult");
        Console.WriteLine("JVInit rc=" + rc);

        if (rc != 0)
        {
            Console.ReadLine();
            return;
        }

        var jockeyBelongMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var trainerBelongMap = new Dictionary<string, string>(StringComparer.Ordinal);

        // 騎手所属・調教師所属を先にマスタから取得する。
        // KS = 騎手マスタ、CH = 調教師マスタを想定。
        LoadBelongMasters(jv, raceFromTime, OpenOption, jockeyBelongMap, trainerBelongMap);

        ExportRaceResult(
            jv,
            raceFromTime,
            OpenOption,
            outCsv,
            startDate,
            endDate,
            jockeyBelongMap,
            trainerBelongMap
        );

        Console.WriteLine("Press Enter...");
        Console.ReadLine();
    }

    // =========================
    // 騎手・調教師所属マスタ読み込み
    // =========================

    static void LoadBelongMasters(
        dynamic jv,
        string fromTime,
        int option,
        Dictionary<string, string> jockeyBelongMap,
        Dictionary<string, string> trainerBelongMap
    )
    {
        Console.WriteLine("所属マスタ読み込み開始 DIFF");

        object ksParser = CreateParserForRecIdFlexible("KS");
        object chParser = CreateParserForRecIdFlexible("CH");

        if (ksParser == null)
        {
            Console.WriteLine("[WARN] KS 騎手マスタのパーサ型が見つかりません。jockey_belong_area はNULLになりやすくなります。");
            DumpTypesHint("KS");
        }

        if (chParser == null)
        {
            Console.WriteLine("[WARN] CH 調教師マスタのパーサ型が見つかりません。trainer_belong_area はSE側TozaiCDで補完します。");
            DumpTypesHint("CH");
        }

        if (ksParser == null && chParser == null)
        {
            Console.WriteLine("[WARN] KS/CH のどちらも見つからないため、所属マスタ読み込みをスキップします。");
            return;
        }

        int readCount = 0;
        int downloadCount = 0;
        string lastTs = "";

        int rc = jv.JVOpen("DIFF", fromTime, option, ref readCount, ref downloadCount, ref lastTs);
        Console.WriteLine("JVOpen(DIFF) rc=" + rc + ", read=" + readCount + ", dl=" + downloadCount + ", lastTs=" + lastTs);

        if (rc != 0)
        {
            Console.WriteLine("[WARN] JVOpen(DIFF)に失敗しました。rc=" + rc);
            Console.WriteLine("[WARN] 所属マスタなしでRACE取得に進みます。");
            return;
        }

        if (downloadCount > 0)
        {
            Console.WriteLine("DIFFにダウンロード対象があります。少し待機します。dl=" + downloadCount);
            Thread.Sleep(InitialDownloadWaitMs);
        }

        object objBuff = Array.Empty<byte>();
        var sjis = Encoding.GetEncoding("shift_jis");

        int downloadWaitCount = 0;
        int sehRetryCount = 0;
        int fileSwitchCount = 0;
        int ksCount = 0;
        int chCount = 0;
        int ksNoBelongCount = 0;
        int chNoBelongCount = 0;

        while (true)
        {
            int grc;
            string fileName = "";

            try
            {
                grc = jv.JVGets(ref objBuff, BUFSIZE, out fileName);
            }
            catch (SEHException ex)
            {
                sehRetryCount++;

                Console.WriteLine("JVGets(DIFF)でSEHExceptionが発生しました。retry=" + sehRetryCount);
                Console.WriteLine(ex.Message);

                if (sehRetryCount >= MaxSehRetryCount)
                {
                    Console.WriteLine("JVGets(DIFF)のSEHExceptionリトライ上限に達したため、所属マスタ読み込みを停止します。");
                    break;
                }

                Thread.Sleep(DownloadWaitMs);
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine("JVGets(DIFF)で例外が発生しました。");
                Console.WriteLine(ex.Message);
                break;
            }

            if (grc == 0)
            {
                break;
            }

            if (grc == -1)
            {
                fileSwitchCount++;

                if (fileSwitchCount % 100 == 1)
                {
                    Console.WriteLine("JVGets(DIFF) ファイル切替 rc=-1 count=" + fileSwitchCount + " file=" + fileName);
                }

                continue;
            }

            if (grc == -3)
            {
                downloadWaitCount++;

                Console.WriteLine("JVGets(DIFF) ダウンロード中 rc=-3。待機します。wait=" + downloadWaitCount);

                if (downloadWaitCount >= MaxDownloadWaitCount)
                {
                    Console.WriteLine("JVGets(DIFF) ダウンロード待機が上限に達しました。所属マスタ読み込みを停止します。");
                    break;
                }

                Thread.Sleep(DownloadWaitMs);
                continue;
            }

            if (grc < 0)
            {
                Console.WriteLine("JVGets(DIFF)エラー rc=" + grc + " file=" + fileName);
                break;
            }

            downloadWaitCount = 0;
            sehRetryCount = 0;

            var bytes = (byte[])objBuff;
            string str = (grc > 0 && grc <= bytes.Length)
                ? sjis.GetString(bytes, 0, grc)
                : sjis.GetString(bytes);

            if (str.Length < 2)
            {
                continue;
            }

            string recId = str.Substring(0, 2);

            if (recId == "KS" && ksParser != null)
            {
                try
                {
                    InvokeSetDataB(ksParser, ref str);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("KS解析失敗 file=" + fileName + " ex=" + ex.Message);
                    continue;
                }

                var ksDict = new Dictionary<string, string>(StringComparer.Ordinal);
                FlattenObject(ksParser, "", ksDict, new HashSet<int>());

                string jockeyId = GetJockeyIdFromMaster(ksDict);
                string belongArea = GetBelongAreaFromMaster(ksDict, "Kisyu", "Jockey");

                if (jockeyId.Length != 0 && belongArea.Length != 0)
                {
                    jockeyBelongMap[jockeyId] = belongArea;
                    ksCount++;
                }
                else
                {
                    ksNoBelongCount++;
                }
            }
            else if (recId == "CH" && chParser != null)
            {
                try
                {
                    InvokeSetDataB(chParser, ref str);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CH解析失敗 file=" + fileName + " ex=" + ex.Message);
                    continue;
                }

                var chDict = new Dictionary<string, string>(StringComparer.Ordinal);
                FlattenObject(chParser, "", chDict, new HashSet<int>());

                string trainerId = GetTrainerIdFromMaster(chDict);
                string belongArea = GetBelongAreaFromMaster(chDict, "Chokyosi", "Trainer");

                if (trainerId.Length != 0 && belongArea.Length != 0)
                {
                    trainerBelongMap[trainerId] = belongArea;
                    chCount++;
                }
                else
                {
                    chNoBelongCount++;
                }
            }
        }

        try
        {
            jv.JVClose();
        }
        catch
        {
        }

        Console.WriteLine("所属マスタ読み込み完了");
        Console.WriteLine("jockeyBelongMap count=" + jockeyBelongMap.Count + ", KS loaded=" + ksCount + ", KS no belong=" + ksNoBelongCount);
        Console.WriteLine("trainerBelongMap count=" + trainerBelongMap.Count + ", CH loaded=" + chCount + ", CH no belong=" + chNoBelongCount);
    }

    static string GetJockeyIdFromMaster(Dictionary<string, string> d)
    {
        string v = Pick(
            d,
            "KisyuCode",
            "KisyuCD",
            "KisyuCd",
            "JockeyCode",
            "JockeyCD",
            "JockeyCd"
        );

        if (v.Length == 0)
        {
            v = PickByKeyContains(d, "KisyuCode", "KisyuCD", "JockeyCode", "JockeyCD");
        }

        return ToIntText(v);
    }

    static string GetTrainerIdFromMaster(Dictionary<string, string> d)
    {
        string v = Pick(
            d,
            "ChokyosiCode",
            "ChokyosiCD",
            "ChokyosiCd",
            "TrainerCode",
            "TrainerCD",
            "TrainerCd"
        );

        if (v.Length == 0)
        {
            v = PickByKeyContains(d, "ChokyosiCode", "ChokyosiCD", "TrainerCode", "TrainerCD");
        }

        return ToIntText(v);
    }

    static string GetBelongAreaFromMaster(Dictionary<string, string> d, params string[] ownerTokens)
    {
        string v = Pick(
            d,
            "TozaiCD",
            "TozaiCd",
            "Tozai",
            "BelongArea",
            "BelongAreaCD",
            "BelongAreaCd",
            "SyozokuCD",
            "SyozokuCd",
            "ShozokuCD",
            "ShozokuCd"
        );

        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        foreach (string ownerToken in ownerTokens)
        {
            v = PickByKeyContainsAll(d, ownerToken, "Tozai");
            if (v.Length != 0)
            {
                return NormalizeBelongArea(v);
            }

            v = PickByKeyContainsAll(d, ownerToken, "Belong");
            if (v.Length != 0)
            {
                return NormalizeBelongArea(v);
            }

            v = PickByKeyContainsAll(d, ownerToken, "Syozoku");
            if (v.Length != 0)
            {
                return NormalizeBelongArea(v);
            }

            v = PickByKeyContainsAll(d, ownerToken, "Shozoku");
            if (v.Length != 0)
            {
                return NormalizeBelongArea(v);
            }
        }

        v = PickByKeyContains(d, "TozaiCD", "Tozai", "BelongArea", "Syozoku", "Shozoku");

        return NormalizeBelongArea(v);
    }

    static string GetJockeyBelongAreaFromSe(Dictionary<string, string> se)
    {
        string v = Pick(
            se,
            "KisyuTozaiCD",
            "KisyuTozaiCd",
            "KisyuTozai",
            "JockeyTozaiCD",
            "JockeyTozaiCd",
            "JockeyTozai",
            "KisyuBelongArea",
            "KisyuBelongAreaCD",
            "JockeyBelongArea",
            "JockeyBelongAreaCD"
        );

        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Kisyu", "Tozai");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Jockey", "Tozai");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Kisyu", "Belong");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Jockey", "Belong");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        return "";
    }

    static string GetTrainerBelongAreaFromSe(Dictionary<string, string> se)
    {
        string v = Pick(
            se,
            "ChokyosiTozaiCD",
            "ChokyosiTozaiCd",
            "ChokyosiTozai",
            "TrainerTozaiCD",
            "TrainerTozaiCd",
            "TrainerTozai",
            "ChokyosiBelongArea",
            "ChokyosiBelongAreaCD",
            "TrainerBelongArea",
            "TrainerBelongAreaCD"
        );

        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Chokyosi", "Tozai");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Trainer", "Tozai");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Chokyosi", "Belong");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        v = PickByKeyContainsAll(se, "Trainer", "Belong");
        if (v.Length != 0)
        {
            return NormalizeBelongArea(v);
        }

        // SEのTozaiCDは従来belong_areaに使っていた値。
        // レース結果SEでは調教師・厩舎所属寄りの値として扱う。
        v = Pick(se, "TozaiCD", "Tozai");

        return NormalizeBelongArea(v);
    }

    // =========================
    // レース結果 RACE 読み込み
    // =========================

    static void ExportRaceResult(
        dynamic jv,
        string fromTime,
        int option,
        string outCsv,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, string> jockeyBelongMap,
        Dictionary<string, string> trainerBelongMap
    )
    {
        int readCount = 0;
        int downloadCount = 0;
        string lastTs = "";

        int rc = jv.JVOpen("RACE", fromTime, option, ref readCount, ref downloadCount, ref lastTs);
        Console.WriteLine("JVOpen(RACE) rc=" + rc + ", read=" + readCount + ", dl=" + downloadCount + ", lastTs=" + lastTs);

        if (rc != 0)
        {
            Console.WriteLine("JVOpen(RACE)失敗 rc=" + rc);
            jv.JVClose();
            return;
        }

        if (downloadCount > 0)
        {
            Console.WriteLine("RACEにダウンロード対象があります。少し待機します。dl=" + downloadCount);
            Thread.Sleep(InitialDownloadWaitMs);
        }

        object objBuff = Array.Empty<byte>();
        var sjis = Encoding.GetEncoding("shift_jis");

        var ra = new JV_RA_RACE();

        object seParser = CreateParserForRecIdFlexible("SE");
        if (seParser == null)
        {
            Console.WriteLine("SE のパーサ型が見つかりません。");
            DumpTypesHint("SE");
            jv.JVClose();
            return;
        }

        // 修正方針:
        // 以前のコードでは SyussoTosu 件数に達した時点で raMap を削除していた。
        // しかし、SyussoTosu が「実際に出走した頭数」寄りで、
        // SE側には取消・除外馬も含まれる場合、最後の馬番が欠落する。
        //
        // そのため今回は、RAとSEをすべて読み込んでから、
        // レース単位で entry / only_hinba を確定してCSV出力する。
        var raMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var seMap = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);

        int raCount = 0;
        int seCount = 0;
        int skippedRaDateCount = 0;
        int skippedSeDateCount = 0;
        int skippedRaPlaceCount = 0;
        int skippedSePlaceCount = 0;
        int downloadWaitCount = 0;
        int sehRetryCount = 0;
        int fileSwitchCount = 0;

        while (true)
        {
            int grc;
            string fileName = "";

            try
            {
                grc = jv.JVGets(ref objBuff, BUFSIZE, out fileName);
            }
            catch (SEHException ex)
            {
                sehRetryCount++;

                Console.WriteLine("JVGets(RACE)でSEHExceptionが発生しました。retry=" + sehRetryCount);
                Console.WriteLine(ex.Message);

                if (sehRetryCount >= MaxSehRetryCount)
                {
                    Console.WriteLine("JVGets(RACE)のSEHExceptionリトライ上限に達したため停止します。");
                    break;
                }

                Thread.Sleep(DownloadWaitMs);
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine("JVGets(RACE)で例外が発生しました。");
                Console.WriteLine(ex.Message);
                break;
            }

            if (grc == 0)
            {
                break;
            }

            if (grc == -1)
            {
                // rc=-1 はエラーではなく、JVDファイルの切り替わり
                fileSwitchCount++;

                if (fileSwitchCount % 100 == 1)
                {
                    Console.WriteLine("JVGets(RACE) ファイル切替 rc=-1 count=" + fileSwitchCount + " file=" + fileName);
                }

                continue;
            }

            if (grc == -3)
            {
                // rc=-3 はファイルダウンロード中
                downloadWaitCount++;

                Console.WriteLine("JVGets(RACE) ダウンロード中 rc=-3。待機します。wait=" + downloadWaitCount);

                if (downloadWaitCount >= MaxDownloadWaitCount)
                {
                    Console.WriteLine("JVGets(RACE) ダウンロード待機が上限に達しました。処理を停止します。");
                    break;
                }

                Thread.Sleep(DownloadWaitMs);
                continue;
            }

            if (grc < 0)
            {
                Console.WriteLine("JVGets(RACE)エラー rc=" + grc + " file=" + fileName);
                break;
            }

            downloadWaitCount = 0;
            sehRetryCount = 0;

            var bytes = (byte[])objBuff;
            string str = (grc > 0 && grc <= bytes.Length)
                ? sjis.GetString(bytes, 0, grc)
                : sjis.GetString(bytes);

            if (str.Length < 2)
            {
                continue;
            }

            string recId = str.Substring(0, 2);

            if (recId == "RA")
            {
                try
                {
                    ra.SetDataB(ref str);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RA解析失敗 file=" + fileName + " ex=" + ex.Message);
                    continue;
                }

                var raDict = new Dictionary<string, string>(StringComparer.Ordinal);
                FlattenObject(ra, "", raDict, new HashSet<int>());

                DateTime raceDate;
                if (!TryGetRaceDate(raDict, out raceDate))
                {
                    skippedRaDateCount++;
                    continue;
                }

                if (raceDate < startDate.Date || raceDate > endDate.Date)
                {
                    skippedRaDateCount++;
                    continue;
                }

                string raceId = MakeRaceId(raDict);
                if (raceId.Length == 0)
                {
                    continue;
                }

                string mergeKey = MakeMergeKey(raDict);
                if (mergeKey.Length == 0)
                {
                    continue;
                }

                string placeCode = Pad2(Pick(raDict, "JyoCD"));
                if (!IsJraPlace(placeCode))
                {
                    skippedRaPlaceCount++;
                    continue;
                }

                raMap[mergeKey] = raDict;
                raCount++;
            }
            else if (recId == "SE")
            {
                try
                {
                    InvokeSetDataB(seParser, ref str);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SE解析失敗 file=" + fileName + " ex=" + ex.Message);
                    continue;
                }

                var seDict = new Dictionary<string, string>(StringComparer.Ordinal);
                FlattenObject(seParser, "", seDict, new HashSet<int>());

                DateTime raceDate;
                if (!TryGetRaceDate(seDict, out raceDate))
                {
                    skippedSeDateCount++;
                    continue;
                }

                if (raceDate < startDate.Date || raceDate > endDate.Date)
                {
                    skippedSeDateCount++;
                    continue;
                }

                string raceId = MakeRaceId(seDict);
                if (raceId.Length == 0)
                {
                    continue;
                }

                string mergeKey = MakeMergeKey(seDict);
                if (mergeKey.Length == 0)
                {
                    continue;
                }

                string placeCode = Pad2(Pick(seDict, "JyoCD"));
                if (!IsJraPlace(placeCode))
                {
                    skippedSePlaceCount++;
                    continue;
                }

                if (!seMap.TryGetValue(mergeKey, out var list))
                {
                    list = new List<Dictionary<string, string>>();
                    seMap[mergeKey] = list;
                }

                list.Add(seDict);
                seCount++;
            }
        }

        jv.JVClose();

        Console.WriteLine("RA loaded count=" + raCount);
        Console.WriteLine("SE loaded count=" + seCount);
        Console.WriteLine("RA race count=" + raMap.Count);
        Console.WriteLine("SE race count=" + seMap.Count);
        Console.WriteLine("skipped RA date count=" + skippedRaDateCount);
        Console.WriteLine("skipped SE date count=" + skippedSeDateCount);
        Console.WriteLine("skipped RA place count=" + skippedRaPlaceCount);
        Console.WriteLine("skipped SE place count=" + skippedSePlaceCount);

        WriteCsvAfterLoad(outCsv, raMap, seMap, jockeyBelongMap, trainerBelongMap);

        Console.WriteLine("done: " + outCsv);
    }

    // =========================
    // 読み込み後CSV出力
    // =========================

    static void WriteCsvAfterLoad(
        string outCsv,
        Dictionary<string, Dictionary<string, string>> raMap,
        Dictionary<string, List<Dictionary<string, string>>> seMap,
        Dictionary<string, string> jockeyBelongMap,
        Dictionary<string, string> trainerBelongMap
    )
    {
        var headers = new[]
        {
            "race_id",
            "year",
            "month",
            "day",
            "weekday",
            "kai",
            "nitime",
            "race_number",
            "race_name",
            "place",
            "course_distance",
            "track",
            "course_type",
            "horseage_conditions",
            "race_class",
            "grade",
            "weight_type",
            "only_hinba",
            "weather",
            "turf_condition",
            "dirt_condition",
            "start_race_time",
            "entry",
            "wakuban",
            "umaban",
            "horse_name",
            "horse_id",
            "sex",
            "horse_age",
            "horse_weight",
            "horse_weight_increase",
            "carried_weight",
            "jockey",
            "jockey_id",
            "jockey_belong_area",
            "belong_area",
            "trainer",
            "trainer_id",
            "trainer_belong_area",
            "abnormal_code",
            "rank",
            "race_time",
            "corner_1_rank",
            "corner_2_rank",
            "corner_3_rank",
            "corner_4_rank",
            "last_3_furlong_time",
            "time_lag"
        };

        int outputCount = 0;
        int raceOutputCount = 0;
        int raceWithoutSeCount = 0;
        int seWithoutRaRaceCount = 0;

        using (var sw = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
        {
            sw.WriteLine(string.Join(",", headers.Select(Csv)));

            foreach (var kv in raMap.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                string mergeKey = kv.Key;
                var raDict = kv.Value;

                if (!seMap.TryGetValue(mergeKey, out var seList) || seList.Count == 0)
                {
                    raceWithoutSeCount++;
                    continue;
                }

                string raceId = MakeRaceId(raDict);
                if (raceId.Length == 0)
                {
                    continue;
                }

                // entry はそのレースのSE件数を使う。
                // 取消・除外を含む「CSVに出す出馬表上の頭数」として扱う。
                int entryCount = seList.Count;

                // only_hinba はレース単位で確定する。
                // RAに牝馬限定の記号・文字がある、または全SEのsexが2なら1。
                string onlyHinba = ConvertOnlyHinbaByRace(raDict, seList);

                var sortedSeList = seList
                    .OrderBy(x => PickInt(x, "Uma", "UmaNum", "Umaban"))
                    .ThenBy(x => ToIntText(Pick(x, "KettoNum")))
                    .ToList();

                foreach (var seDict in sortedSeList)
                {
                    WriteRaceRow(
                        sw,
                        raceId,
                        raDict,
                        seDict,
                        entryCount,
                        onlyHinba,
                        jockeyBelongMap,
                        trainerBelongMap
                    );

                    outputCount++;
                }

                raceOutputCount++;
            }
        }

        foreach (var kv in seMap)
        {
            if (!raMap.ContainsKey(kv.Key))
            {
                seWithoutRaRaceCount++;
            }
        }

        Console.WriteLine("output race count=" + raceOutputCount);
        Console.WriteLine("output row count=" + outputCount);
        Console.WriteLine("race without SE count=" + raceWithoutSeCount);
        Console.WriteLine("SE without RA race count=" + seWithoutRaRaceCount);
    }

    // =========================
    // CSV出力
    // =========================

    static void WriteRaceRow(
        StreamWriter sw,
        string raceId,
        Dictionary<string, string> ra,
        Dictionary<string, string> se,
        int entryCount,
        string onlyHinba,
        Dictionary<string, string> jockeyBelongMap,
        Dictionary<string, string> trainerBelongMap
    )
    {
        string year = ToIntText(Pick(ra, "Year"));

        string monthDay = Pick(ra, "MonthDay");
        string month = monthDay.Length >= 2 ? ToIntText(monthDay.Substring(0, 2)) : "";
        string day = monthDay.Length >= 4 ? ToIntText(monthDay.Substring(2, 2)) : "";

        DateTime raceDate;
        string weekday = "";
        if (TryGetRaceDate(ra, out raceDate))
        {
            weekday = GetWeekdayCode(raceDate);
        }

        string kai = ToIntText(Pick(ra, "Kaiji"));
        string nitime = ToIntText(Pick(ra, "Nichiji"));
        string raceNumber = ToIntText(Pick(ra, "RaceNum"));

        string place = ToIntText(Pick(ra, "JyoCD"));

        string courseDistance = ToIntText(Pick(ra, "Kyori", "Distance"));
        string track = ToIntText(Pick(ra, "TrackCD"));

        string rawCourseKubun = Pick(ra, "CourseKubunCD", "CourseKubun", "CourseCD");
        if (rawCourseKubun.Length == 0)
        {
            rawCourseKubun = PickByKeyContains(ra, "CourseKubun");
        }
        string courseType = ConvertCourseType(rawCourseKubun);

        // horseage_conditions は SyubetuCD だけを元に判定する。
        // レース名文字列からの推測はしない。
        string syubetuCode = GetSyubetuCode(ra);
        string horseageConditions = ConvertHorseAgeConditionsFromSyubetuCode(syubetuCode);

        // race_class は JyokenCD5 を元に判定する。
        string jyokenCode5 = GetJyokenCode5(ra);

        string gradeRaw = Pick(ra, "GradeCD", "Grade");
        string grade = ConvertGrade(gradeRaw);

        string raceClass = ConvertRaceClassFromJyokenCode5(jyokenCode5, gradeRaw);

        string jyuryoRaw = Pick(ra, "JyuryoCD", "WeightTypeCD");
        if (jyuryoRaw.Length == 0)
        {
            jyuryoRaw = PickByKeyContains(ra, "Jyuryo");
        }
        string weightType = ConvertWeightType(jyuryoRaw);

        string tenkoRaw = Pick(ra, "TenkoCD");
        string weather = ToIntText(tenkoRaw);

        string shibaBabaRaw = Pick(ra, "SibaBabaCD", "TurfBabaCD");
        string dirtBabaRaw = Pick(ra, "DirtBabaCD");

        string turfCondition = "";
        string dirtCondition = "";

        if (IsTurfTrack(track))
        {
            turfCondition = ToIntText(shibaBabaRaw);
            dirtCondition = "";
        }
        else if (IsDirtTrack(track))
        {
            turfCondition = "";
            dirtCondition = ToIntText(dirtBabaRaw);
        }
        else
        {
            turfCondition = "";
            dirtCondition = "";
        }

        string startRaceTime = FormatTimeHHMMSS(Pick(ra, "HassoTime", "HassoJikoku", "StartTime"));

        // 修正:
        // 以前は RA の SyussoTosu を entry にしていた。
        // 今回は、読み込み済みの同一レースSE件数を entry にする。
        // これにより、取消・除外馬を含む出馬表上の件数とCSV行数が一致する。
        string entry = entryCount > 0
            ? entryCount.ToString()
            : ToIntText(Pick(ra, "SyussoTosu", "ShussoTosu", "Tosu", "HeadCount"));

        string raceNameRaw = Pick(ra, "Hondai");
        if (raceNameRaw.Length == 0)
        {
            raceNameRaw = PickByKeyContains(ra, "Hondai", "RaceName", "Ryakusyo10");
        }

        string raceName = BuildRaceName(raceNameRaw, horseageConditions, raceClass);

        string wakuban = ToIntText(Pick(se, "Waku", "WakuNum", "Wakuban"));
        string umaban = ToIntText(Pick(se, "Uma", "UmaNum", "Umaban"));

        string horseName = Pick(se, "Bamei", "UmaName", "HorseName");
        string horseId = ToIntText(Pick(se, "KettoNum"));

        string sex = ToIntText(Pick(se, "SexCD", "Sex"));
        string horseAge = ToIntText(Pick(se, "Barei", "Age"));

        string horseWeightRaw = Pick(se, "Bataijyu", "Bataiju", "BodyWeight");
        if (horseWeightRaw.Length == 0)
        {
            horseWeightRaw = PickByKeyContains(se, "Bataijyu", "Bataiju", "BodyWeight", "Taiju");
        }
        string horseWeight = NormalizeBodyWeight(horseWeightRaw);

        string zogenFugo = Pick(se, "ZogenFugo", "ZougenFugo");
        if (zogenFugo.Length == 0)
        {
            zogenFugo = PickByKeyContains(se, "ZogenFugo", "ZougenFugo");
        }

        string zogenSa = Pick(se, "ZogenSa", "ZougenSa");
        if (zogenSa.Length == 0)
        {
            zogenSa = PickByKeyContains(se, "ZogenSa", "ZougenSa");
        }

        string horseWeightIncrease = FormatZogenToIntText(zogenFugo, zogenSa);

        string carriedWeight = Pick(se, "Futan", "Kinryo", "LoadWeight");
        if (carriedWeight.Length == 0)
        {
            carriedWeight = PickByKeyContains(se, "Futan", "Kinryo", "Load");
        }
        carriedWeight = ToIntText(carriedWeight);

        string jockeyId = Pick(se, "KisyuCode", "KisyuCD", "JockeyCode", "JockeyCD");
        if (jockeyId.Length == 0)
        {
            jockeyId = PickByKeyContains(se, "KisyuCode", "JockeyCode", "Kisyu");
        }
        jockeyId = ToIntText(jockeyId);

        string jockey = Pick(se, "KisyuRyakusyo", "KisyuName", "JockeyRyakusyo", "JockeyName");
        if (jockey.Length == 0)
        {
            jockey = PickByKeyContains(se, "KisyuRyakusyo", "JockeyName", "Kisyu");
        }

        string jockeyBelongArea = "";

        if (jockeyId.Length != 0 && jockeyBelongMap.TryGetValue(jockeyId, out var jockeyBelongFromMaster))
        {
            jockeyBelongArea = NormalizeBelongArea(jockeyBelongFromMaster);
        }

        if (jockeyBelongArea.Length == 0)
        {
            jockeyBelongArea = GetJockeyBelongAreaFromSe(se);
        }

        string trainerId = Pick(se, "ChokyosiCode", "ChokyosiCD", "TrainerCode", "TrainerCD");
        if (trainerId.Length == 0)
        {
            trainerId = PickByKeyContains(se, "ChokyosiCode", "TrainerCode", "Chokyosi");
        }
        trainerId = ToIntText(trainerId);

        string trainer = Pick(se, "ChokyosiRyakusyo", "ChokyosiName", "TrainerRyakusyo", "TrainerName");
        if (trainer.Length == 0)
        {
            trainer = PickByKeyContains(se, "ChokyosiRyakusyo", "TrainerName", "Chokyosi");
        }

        string trainerBelongArea = "";

        if (trainerId.Length != 0 && trainerBelongMap.TryGetValue(trainerId, out var trainerBelongFromMaster))
        {
            trainerBelongArea = NormalizeBelongArea(trainerBelongFromMaster);
        }

        if (trainerBelongArea.Length == 0)
        {
            trainerBelongArea = GetTrainerBelongAreaFromSe(se);
        }

        // 既存互換用の belong_area。
        // 従来はSE側のTozaiCDを使っていたため、ここでは trainer_belong_area と同じ値を基本にする。
        string belongArea = trainerBelongArea;

        if (belongArea.Length == 0)
        {
            belongArea = NormalizeBelongArea(Pick(se, "TozaiCD", "Tozai"));
        }

        string abnormalCode = Pick(se, "IjyoCD", "IjyoKubunCD", "IjouCD", "AbnormalCD");
        if (abnormalCode.Length == 0)
        {
            abnormalCode = PickByKeyContains(se, "Ijyo", "Ijou", "Abnormal");
        }
        abnormalCode = ToIntText(abnormalCode);

        string rank = Pick(se, "KakuteiJyuni", "KakuteiJuni", "KakuteiRank", "KakuteiOrder");
        if (rank.Length == 0)
        {
            rank = PickByKeyContains(se, "Kakutei");
        }
        rank = ToIntText(rank);

        string raceTime = Pick(se, "RaceTime", "Time", "SohaTime");
        if (raceTime.Length == 0)
        {
            raceTime = PickByKeyContainsAll(se, "Race", "Time");
        }

        if (raceTime.Length == 0)
        {
            raceTime = PickByKeyContainsAll(se, "Soha", "Time");
        }
        raceTime = NormalizeRaceTimeToTenths(raceTime);

        string corner1 = Pick(se, "Jyuni1", "Corner1Jyuni", "Corner1Juni", "Corner1");
        if (corner1.Length == 0) corner1 = PickByKeyContainsAll(se, "Jyuni", "1");
        if (corner1.Length == 0) corner1 = PickByKeyContainsAll(se, "Juni", "1");
        if (corner1.Length == 0) corner1 = PickByKeyContainsAll(se, "Corner", "1");
        corner1 = ToIntText(corner1);

        string corner2 = Pick(se, "Jyuni2", "Corner2Jyuni", "Corner2Juni", "Corner2");
        if (corner2.Length == 0) corner2 = PickByKeyContainsAll(se, "Jyuni", "2");
        if (corner2.Length == 0) corner2 = PickByKeyContainsAll(se, "Juni", "2");
        if (corner2.Length == 0) corner2 = PickByKeyContainsAll(se, "Corner", "2");
        corner2 = ToIntText(corner2);

        string corner3 = Pick(se, "Jyuni3", "Corner3Jyuni", "Corner3Juni", "Corner3");
        if (corner3.Length == 0) corner3 = PickByKeyContainsAll(se, "Jyuni", "3");
        if (corner3.Length == 0) corner3 = PickByKeyContainsAll(se, "Juni", "3");
        if (corner3.Length == 0) corner3 = PickByKeyContainsAll(se, "Corner", "3");
        corner3 = ToIntText(corner3);

        string corner4 = Pick(se, "Jyuni4", "Corner4Jyuni", "Corner4Juni", "Corner4");
        if (corner4.Length == 0) corner4 = PickByKeyContainsAll(se, "Jyuni", "4");
        if (corner4.Length == 0) corner4 = PickByKeyContainsAll(se, "Juni", "4");
        if (corner4.Length == 0) corner4 = PickByKeyContainsAll(se, "Corner", "4");
        corner4 = ToIntText(corner4);

        string last3F = Pick(se, "HaronTimeL3", "Ato3HaronTime", "After3HaronTime", "Last3F", "Agari3F");
        if (last3F.Length == 0) last3F = PickByKeyContainsAll(se, "Haron", "L3");
        if (last3F.Length == 0) last3F = PickByKeyContains(se, "Last3F", "Agari3F", "3Haron");
        last3F = NormalizeTenthsTime(last3F);

        string timeLag = Pick(se, "TimeDiff", "TimeSa", "RaceTimeDiff");
        if (timeLag.Length == 0) timeLag = PickByKeyContainsAll(se, "Time", "Diff");
        if (timeLag.Length == 0) timeLag = PickByKeyContains(se, "TimeSa");
        timeLag = ToIntText(timeLag);

        var row = new[]
        {
            ToNullLiteral(raceId),
            ToNullLiteral(year),
            ToNullLiteral(month),
            ToNullLiteral(day),
            ToNullLiteral(weekday),
            ToNullLiteral(kai),
            ToNullLiteral(nitime),
            ToNullLiteral(raceNumber),
            ToNullLiteral(raceName),
            ToNullLiteral(place),
            ToNullLiteral(courseDistance),
            ToNullLiteral(track),
            ToNullLiteral(courseType),
            ToNullLiteral(horseageConditions),
            ToNullLiteral(raceClass),
            ToNullLiteral(grade),
            ToNullLiteral(weightType),
            ToNullLiteral(onlyHinba),
            ToNullLiteral(weather),
            ToNullLiteral(turfCondition),
            ToNullLiteral(dirtCondition),
            ToNullLiteral(startRaceTime),
            ToNullLiteral(entry),
            ToNullLiteral(wakuban),
            ToNullLiteral(umaban),
            ToNullLiteral(horseName),
            ToNullLiteral(horseId),
            ToNullLiteral(sex),
            ToNullLiteral(horseAge),
            ToNullLiteral(horseWeight),
            ToNullLiteral(horseWeightIncrease),
            ToNullLiteral(carriedWeight),
            ToNullLiteral(jockey),
            ToNullLiteral(jockeyId),
            ToNullLiteral(jockeyBelongArea),
            ToNullLiteral(belongArea),
            ToNullLiteral(trainer),
            ToNullLiteral(trainerId),
            ToNullLiteral(trainerBelongArea),
            ToNullLiteral(abnormalCode),
            ToNullLiteral(rank),
            ToNullLiteral(raceTime),
            ToNullLiteral(corner1),
            ToNullLiteral(corner2),
            ToNullLiteral(corner3),
            ToNullLiteral(corner4),
            ToNullLiteral(last3F),
            ToNullLiteral(timeLag)
        };

        sw.WriteLine(string.Join(",", row.Select(Csv)));
    }

    // =========================
    // horseage_conditions / race_class 変換
    // =========================

    static string GetSyubetuCode(Dictionary<string, string> ra)
    {
        string v = Pick(ra, "SyubetuCD", "SyubetuCode", "RaceSyubetuCD", "RaceKindCD");

        if (v.Length != 0)
        {
            return NormalizeCodeNumber(v);
        }

        v = PickByKeyContains(ra, "SyubetuCD", "SyubetuCode", "RaceKindCD");

        return NormalizeCodeNumber(v);
    }

    static string ConvertHorseAgeConditionsFromSyubetuCode(string syubetuCode)
    {
        syubetuCode = NormalizeCodeNumber(syubetuCode);

        // horseage_conditions
        // 2歳限定=0
        // 3歳限定=1
        // 3歳以上=2
        // 4歳以上=3
        // その他=4
        //
        // ここはレース名文字列ではなく、JRA-VANの競走種別コード SyubetuCD を元に判定する。
        //
        // 主に使用する想定:
        // 11 = サラ系2歳
        // 12 = サラ系3歳
        // 13 = サラ系3歳以上
        // 14 = サラ系4歳以上
        // 18 = サラ系障害3歳以上
        // 19 = サラ系障害4歳以上
        //
        // 古いアラブ系などが混ざった場合も一応 21〜24 を同じ考えで処理する。

        switch (syubetuCode)
        {
            case "11":
            case "21":
                return "0";

            case "12":
            case "22":
                return "1";

            case "13":
            case "18":
            case "23":
            case "28":
                return "2";

            case "14":
            case "19":
            case "24":
            case "29":
                return "3";

            default:
                return "4";
        }
    }

    static string GetJyokenCode5(Dictionary<string, string> ra)
    {
        // まず分かりやすい名前を直接拾う
        string direct = Pick(
            ra,
            "JyokenCD5",
            "JokenCD5",
            "JyokenCode5",
            "JokenCode5",
            "JyokenCD[4]",
            "JokenCD[4]"
        );

        if (direct.Length != 0)
        {
            return NormalizeCodeNumber(direct);
        }

        // FlattenObject後のキー例:
        // JyokenInfo.JyokenCD[4]
        // JyokenInfo.JyokenCD[4].Code
        // のような形も拾う
        var indexed = new Dictionary<int, string>();

        foreach (var kv in ra)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            string key = kv.Key;
            string value = Norm(kv.Value);

            if (key.IndexOf("Jyoken", StringComparison.OrdinalIgnoreCase) < 0 &&
                key.IndexOf("Joken", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            string code = NormalizeCodeNumber(value);
            if (code.Length == 0)
            {
                continue;
            }

            int index;
            if (TryExtractArrayIndex(key, out index))
            {
                if (!indexed.ContainsKey(index))
                {
                    indexed[index] = code;
                }

                continue;
            }

            int suffixIndex;
            if (TryExtractJyokenSuffixIndex(key, out suffixIndex))
            {
                if (!indexed.ContainsKey(suffixIndex))
                {
                    indexed[suffixIndex] = code;
                }
            }
        }

        // JRA-VANのJyokenCD配列は0始まりで [4] が5番目の想定
        if (indexed.TryGetValue(4, out var v4))
        {
            return NormalizeCodeNumber(v4);
        }

        // 名前が JyokenCD5 のような1始まりだった場合も上で拾うが、保険として index=5 も見る
        if (indexed.TryGetValue(5, out var v5))
        {
            return NormalizeCodeNumber(v5);
        }

        return "";
    }

    static string ConvertRaceClassFromJyokenCode5(string jyokenCode5, string gradeRaw)
    {
        string code = NormalizeCodeNumber(jyokenCode5);

        // race_class
        // 新馬戦=0
        // 未勝利=1
        // 1勝クラス=2
        // 2勝クラス=3
        // 3勝クラス=4
        // オープン=5
        // 未出走=6

        switch (code)
        {
            case "701":
                return "0";

            case "703":
                return "1";

            case "5":
                return "2";

            case "10":
                return "3";

            case "16":
                return "4";

            case "999":
                return "5";

            case "702":
                return "6";

            default:
                break;
        }

        // JyokenCD5が取れなかった場合でも、GradeCDがあるレースは基本的にオープン以上として扱う
        if (Norm(gradeRaw).Length != 0)
        {
            return "5";
        }

        return "";
    }

    // =========================
    // only_hinba 判定
    // =========================

    static string ConvertOnlyHinbaByRace(Dictionary<string, string> ra, List<Dictionary<string, string>> seList)
    {
        // 1. RA側のレース名・記号・条件系に「牝」が含まれる場合は牝馬限定
        if (RaLooksHinbaLimited(ra))
        {
            return "1";
        }

        // 2. レース名だけでは拾えない牝馬限定重賞対策
        //    例: 桜花賞、秋華賞、優駿牝馬、ヴィクトリアマイル等
        string raceNameRaw = Pick(ra, "Hondai");
        if (raceNameRaw.Length == 0)
        {
            raceNameRaw = PickByKeyContains(ra, "Hondai", "RaceName", "Ryakusyo10");
        }

        if (RaceNameLooksFillyLimited(raceNameRaw))
        {
            return "1";
        }

        // 3. SE側の全馬が sex=2 の場合は牝馬限定として扱う
        //    取消・除外馬も含めて全て牝馬なら1。
        //    sexが取れない行が混ざる場合は、この判定では1にしない。
        if (seList != null && seList.Count > 0)
        {
            bool allKnown = true;
            bool allFemale = true;

            foreach (var se in seList)
            {
                string sex = ToIntText(Pick(se, "SexCD", "Sex"));

                if (sex.Length == 0)
                {
                    allKnown = false;
                    allFemale = false;
                    break;
                }

                if (sex != "2")
                {
                    allFemale = false;
                    break;
                }
            }

            if (allKnown && allFemale)
            {
                return "1";
            }
        }

        return "0";
    }

    static bool RaLooksHinbaLimited(Dictionary<string, string> ra)
    {
        if (ra == null)
        {
            return false;
        }

        foreach (var kv in ra)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            string key = kv.Key;
            string value = kv.Value;

            bool keyLooksRelated =
                key.IndexOf("Kigo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Hinba", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Hondai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Fukudai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("RaceName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Ryakusyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Jyoken", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("Joken", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!keyLooksRelated)
            {
                continue;
            }

            if (value.IndexOf("牝", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    static bool RaceNameLooksFillyLimited(string raceNameRaw)
    {
        string name = Norm(raceNameRaw);

        if (name.Length == 0)
        {
            return false;
        }

        if (name.IndexOf("牝", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        // レース名に「牝」が入らない牝馬限定重賞・主要競走の保険。
        // JRAの番組変更に備え、最終的にはRAの競走記号・条件系で拾えるのが理想。
        string[] knownFillyLimitedNames =
        {
            "桜花賞",
            "優駿牝馬",
            "オークス",
            "秋華賞",
            "阪神ジュベナイルフィリーズ",
            "ヴィクトリアマイル",
            "エリザベス女王杯",
            "チューリップ賞",
            "フィリーズレビュー",
            "フローラステークス",
            "フローラＳ",
            "ローズステークス",
            "ローズＳ",
            "紫苑ステークス",
            "紫苑Ｓ",
            "フェアリーステークス",
            "フェアリーＳ",
            "クイーンカップ",
            "クイーンＣ",
            "アルテミスステークス",
            "アルテミスＳ",
            "ファンタジーステークス",
            "ファンタジーＳ",
            "京都牝馬ステークス",
            "京都牝馬Ｓ",
            "中山牝馬ステークス",
            "中山牝馬Ｓ",
            "福島牝馬ステークス",
            "福島牝馬Ｓ",
            "府中牝馬ステークス",
            "府中牝馬Ｓ",
            "ターコイズステークス",
            "ターコイズＳ",
            "マーメイドステークス",
            "マーメイドＳ",
            "クイーンステークス",
            "クイーンＳ",
            "愛知杯"
        };

        foreach (string s in knownFillyLimitedNames)
        {
            if (name.IndexOf(s, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // =========================
    // その他の変換処理
    // =========================

    static string GetWeekdayCode(DateTime dt)
    {
        if (dt.DayOfWeek == DayOfWeek.Saturday) return "1";
        if (dt.DayOfWeek == DayOfWeek.Sunday) return "2";
        return "3";
    }

    static string ConvertCourseType(string raw)
    {
        raw = Norm(raw).ToUpperInvariant();

        if (raw.Length == 0) return "0";

        if (raw == "A") return "1";
        if (raw == "B") return "2";
        if (raw == "C") return "3";
        if (raw == "D") return "4";

        if (raw == "1") return "1";
        if (raw == "2") return "2";
        if (raw == "3") return "3";
        if (raw == "4") return "4";

        return "0";
    }

    static string ConvertGrade(string raw)
    {
        raw = Norm(raw).ToUpperInvariant();

        if (raw.Length == 0) return "";

        if (raw == "A") return "0"; // G1
        if (raw == "B") return "1"; // G2
        if (raw == "C") return "2"; // G3
        if (raw == "D") return "3"; // 重賞
        if (raw == "E") return "4"; // 特別競走
        if (raw == "F") return "5"; // JG1
        if (raw == "G") return "6"; // JG2
        if (raw == "H") return "7"; // JG3
        if (raw == "L") return "8"; // リステッド

        if (IsSignedInteger(raw))
        {
            return ToIntText(raw);
        }

        return "";
    }

    static string ConvertWeightType(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        if (raw == "ハンデ") return "1";
        if (raw == "別定") return "2";
        if (raw == "馬齢") return "3";
        if (raw == "定量") return "4";

        if (raw == "1" || raw == "2" || raw == "3" || raw == "4")
        {
            return raw;
        }

        return ToIntText(raw);
    }

    static string BuildRaceName(string rawRaceName, string horseageConditions, string raceClass)
    {
        rawRaceName = Norm(rawRaceName);

        if (rawRaceName.Length != 0)
        {
            return rawRaceName;
        }

        string ageLabel = HorseAgeConditionLabel(horseageConditions);
        string classLabel = RaceClassLabel(raceClass);

        string built = (ageLabel + classLabel).Trim();

        if (built.Length != 0)
        {
            return built;
        }

        return "";
    }

    static string HorseAgeConditionLabel(string code)
    {
        if (code == "0") return "2歳";
        if (code == "1") return "3歳";
        if (code == "2") return "3歳以上";
        if (code == "3") return "4歳以上";
        return "";
    }

    static string RaceClassLabel(string code)
    {
        if (code == "0") return "新馬";
        if (code == "1") return "未勝利";
        if (code == "2") return "1勝クラス";
        if (code == "3") return "2勝クラス";
        if (code == "4") return "3勝クラス";
        if (code == "5") return "オープン";
        if (code == "6") return "未出走";
        return "";
    }

    static string NormalizeBelongArea(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        if (raw == "0") return "0";
        if (raw == "1") return "1";
        if (raw == "2") return "2";
        if (raw == "3") return "3";
        if (raw == "4") return "4";

        if (raw.IndexOf("美浦", StringComparison.Ordinal) >= 0) return "1";
        if (raw.IndexOf("栗東", StringComparison.Ordinal) >= 0) return "2";
        if (raw.IndexOf("地方", StringComparison.Ordinal) >= 0) return "3";
        if (raw.IndexOf("海外", StringComparison.Ordinal) >= 0) return "4";

        return ToIntText(raw);
    }

    static bool IsTurfTrack(string track)
    {
        int n;
        if (!int.TryParse(track, out n)) return false;

        // 通常の芝トラック
        if (n >= 10 && n <= 22) return true;

        // 障害は芝扱い寄りにする
        if (n >= 51 && n <= 59) return true;

        return false;
    }

    static bool IsDirtTrack(string track)
    {
        int n;
        if (!int.TryParse(track, out n)) return false;

        // 通常のダートトラック
        if (n >= 23 && n <= 29) return true;

        return false;
    }

    static string FormatTimeHHMMSS(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        string digits = ExtractDigits(raw);

        if (digits.Length == 3)
        {
            digits = "0" + digits;
        }

        if (digits.Length == 4)
        {
            string hh = digits.Substring(0, 2);
            string mm = digits.Substring(2, 2);
            return hh + ":" + mm + ":00";
        }

        if (digits.Length == 6)
        {
            string hh = digits.Substring(0, 2);
            string mm = digits.Substring(2, 2);
            string ss = digits.Substring(4, 2);
            return hh + ":" + mm + ":" + ss;
        }

        return raw;
    }

    static string NormalizeRaceTimeToTenths(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        raw = raw.Replace("．", ".").Replace("：", ":");

        // 例: 2:34:5 → 1545
        // 例: 1:09.9 → 699
        if (raw.IndexOf(":", StringComparison.Ordinal) >= 0)
        {
            string[] parts = raw.Split(':');

            if (parts.Length >= 2)
            {
                int minute;
                if (!int.TryParse(ExtractDigits(parts[0]), out minute))
                {
                    return "";
                }

                int second = 0;
                int tenth = 0;

                if (parts.Length >= 3)
                {
                    if (!int.TryParse(ExtractDigits(parts[1]), out second))
                    {
                        return "";
                    }

                    if (!int.TryParse(ExtractDigits(parts[2]), out tenth))
                    {
                        tenth = 0;
                    }

                    return (minute * 600 + second * 10 + tenth).ToString();
                }
                else
                {
                    string secPart = parts[1];

                    if (secPart.IndexOf(".", StringComparison.Ordinal) >= 0)
                    {
                        string[] secParts = secPart.Split('.');

                        if (!int.TryParse(ExtractDigits(secParts[0]), out second))
                        {
                            return "";
                        }

                        if (secParts.Length >= 2)
                        {
                            string tenthText = ExtractDigits(secParts[1]);
                            if (tenthText.Length > 0)
                            {
                                tenthText = tenthText.Substring(0, 1);
                                int.TryParse(tenthText, out tenth);
                            }
                        }

                        return (minute * 600 + second * 10 + tenth).ToString();
                    }
                    else
                    {
                        string digits = ExtractDigits(secPart);
                        if (!int.TryParse(digits, out second))
                        {
                            return "";
                        }

                        return (minute * 600 + second * 10).ToString();
                    }
                }
            }
        }

        return NormalizeTenthsTime(raw);
    }

    static string NormalizeTenthsTime(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        raw = raw.Replace("．", ".");

        // 例: 34.5 → 345
        if (raw.IndexOf(".", StringComparison.Ordinal) >= 0)
        {
            string[] parts = raw.Split('.');

            string secDigits = parts.Length >= 1 ? ExtractDigits(parts[0]) : "";
            string tenthDigits = parts.Length >= 2 ? ExtractDigits(parts[1]) : "";

            if (secDigits.Length == 0)
            {
                return "";
            }

            if (tenthDigits.Length == 0)
            {
                tenthDigits = "0";
            }
            else
            {
                tenthDigits = tenthDigits.Substring(0, 1);
            }

            int sec;
            int tenth;

            if (!int.TryParse(secDigits, out sec)) return "";
            if (!int.TryParse(tenthDigits, out tenth)) tenth = 0;

            return (sec * 10 + tenth).ToString();
        }

        return ToIntText(raw);
    }

    // =========================
    // パーサ・Reflection
    // =========================

    static object CreateParserForRecIdFlexible(string recId)
    {
        var asm = typeof(JV_RA_RACE).Assembly;
        var types = asm.GetTypes();

        var t1 = types.FirstOrDefault(x => x.Name.StartsWith("JV_" + recId + "_", StringComparison.Ordinal));
        if (t1 != null) return Activator.CreateInstance(t1);

        var t2 = types.FirstOrDefault(x => x.Name.StartsWith("JV_" + recId, StringComparison.Ordinal));
        if (t2 != null) return Activator.CreateInstance(t2);

        var token = "_" + recId + "_";
        var t3 = types.FirstOrDefault(x => x.Name.IndexOf(token, StringComparison.Ordinal) >= 0);
        if (t3 != null) return Activator.CreateInstance(t3);

        return null;
    }

    static void InvokeSetDataB(object parser, ref string s)
    {
        var mi = parser.GetType().GetMethod("SetDataB", BindingFlags.Public | BindingFlags.Instance);
        if (mi == null) return;

        object[] args = new object[] { s };
        mi.Invoke(parser, args);
        s = (string)args[0];
    }

    static void DumpTypesHint(string recId)
    {
        var asm = typeof(JV_RA_RACE).Assembly;
        var token = "_" + recId + "_";

        var cand = asm.GetTypes()
            .Select(x => x.Name)
            .Where(n =>
                n.StartsWith("JV_" + recId, StringComparison.Ordinal) ||
                n.IndexOf(token, StringComparison.Ordinal) >= 0
            )
            .OrderBy(n => n)
            .Take(50)
            .ToList();

        Console.WriteLine("--- type hint for " + recId + " ---");
        foreach (var n in cand) Console.WriteLine(n);
        Console.WriteLine("--------------------------------");
    }

    static void FlattenObject(object obj, string prefix, Dictionary<string, string> dict, HashSet<int> visited)
    {
        if (obj == null)
        {
            return;
        }

        Type t = obj.GetType();

        if (!t.IsValueType && t != typeof(string))
        {
            int key = RuntimeHelpers.GetHashCode(obj);

            if (visited.Contains(key))
            {
                return;
            }

            visited.Add(key);
        }

        if (IsLeafType(t))
        {
            dict[TrimDot(prefix)] = obj.ToString();
            return;
        }

        if (obj is IEnumerable enumerable && !(obj is string))
        {
            int idx = 0;

            foreach (var item in enumerable)
            {
                FlattenObject(item, prefix + "[" + idx + "].", dict, visited);
                idx++;
            }

            return;
        }

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object val;

            try
            {
                val = p.GetValue(obj, null);
            }
            catch
            {
                continue;
            }

            FlattenObject(val, prefix + p.Name + ".", dict, visited);
        }

        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object val;

            try
            {
                val = f.GetValue(obj);
            }
            catch
            {
                continue;
            }

            FlattenObject(val, prefix + f.Name + ".", dict, visited);
        }
    }

    static bool IsLeafType(Type t)
    {
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(Guid);
    }

    static string TrimDot(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (s.EndsWith("."))
        {
            return s.Substring(0, s.Length - 1);
        }

        return s;
    }

    // =========================
    // 日付・ID
    // =========================

    static string ToYmd(DateTime dt)
    {
        return dt.ToString("yyyyMMdd");
    }

    static DateTime GetLastWeekMonday(DateTime baseDate)
    {
        // DayOfWeek は Sunday=0, Monday=1, ... Saturday=6。
        // 月曜始まりの週として扱うため、月曜からの経過日数に変換する。
        int daysSinceMonday = ((int)baseDate.DayOfWeek + 6) % 7;

        // 今週の月曜を求め、そこから7日前へ戻す。
        DateTime thisWeekMonday = baseDate.Date.AddDays(-daysSinceMonday);
        return thisWeekMonday.AddDays(-7);
    }

    static bool Is8Digits(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 8) return false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') return false;
        }

        return true;
    }

    static bool Is14Digits(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 14) return false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') return false;
        }

        return true;
    }

    static bool TryParseYmd(string ymd, out DateTime dt)
    {
        dt = DateTime.MinValue;

        if (!Is8Digits(ymd)) return false;

        int year;
        int month;
        int day;

        if (!int.TryParse(ymd.Substring(0, 4), out year)) return false;
        if (!int.TryParse(ymd.Substring(4, 2), out month)) return false;
        if (!int.TryParse(ymd.Substring(6, 2), out day)) return false;

        try
        {
            dt = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TryGetRaceDate(Dictionary<string, string> d, out DateTime raceDate)
    {
        raceDate = DateTime.MinValue;

        string yearText = Pick(d, "Year");
        string monthDay = Pick(d, "MonthDay");

        if (yearText.Length != 4 || monthDay.Length < 4) return false;

        int year;
        int month;
        int day;

        if (!int.TryParse(yearText, out year)) return false;
        if (!int.TryParse(monthDay.Substring(0, 2), out month)) return false;
        if (!int.TryParse(monthDay.Substring(2, 2), out day)) return false;

        try
        {
            raceDate = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string MakeRaceId(Dictionary<string, string> d)
    {
        string year = Pick(d, "Year");
        string jyo = Pad2(Pick(d, "JyoCD"));
        string kaiji = Pad2(Pick(d, "Kaiji"));
        string nichiji = Pad2(Pick(d, "Nichiji"));
        string raceNum = Pad2(Pick(d, "RaceNum"));

        if (year.Length != 4 || jyo.Length != 2 || kaiji.Length != 2 || nichiji.Length != 2 || raceNum.Length != 2)
        {
            return "";
        }

        return year + jyo + kaiji + nichiji + raceNum;
    }

    static string MakeMergeKey(Dictionary<string, string> d)
    {
        string year = Pick(d, "Year");
        string monthDay = Pick(d, "MonthDay");
        string jyo = Pad2(Pick(d, "JyoCD"));
        string kaiji = Pad2(Pick(d, "Kaiji"));
        string nichiji = Pad2(Pick(d, "Nichiji"));
        string raceNum = Pad2(Pick(d, "RaceNum"));

        if (year.Length != 4 || monthDay.Length < 4 || jyo.Length != 2 || kaiji.Length != 2 || nichiji.Length != 2 || raceNum.Length != 2)
        {
            return "";
        }

        monthDay = monthDay.Substring(0, 4);

        return year + monthDay + jyo + kaiji + nichiji + raceNum;
    }

    static bool IsJraPlace(string placeCode)
    {
        switch (placeCode)
        {
            case "01":
            case "02":
            case "03":
            case "04":
            case "05":
            case "06":
            case "07":
            case "08":
            case "09":
            case "10":
                return true;
            default:
                return false;
        }
    }

    // =========================
    // 値取得ユーティリティ
    // =========================

    static string Pick(Dictionary<string, string> d, params string[] candidatesOrSuffixes)
    {
        if (d == null)
        {
            return "";
        }

        foreach (var c in candidatesOrSuffixes)
        {
            if (d.TryGetValue(c, out var v1) && !string.IsNullOrWhiteSpace(v1))
            {
                return Norm(v1);
            }

            var hit = d.FirstOrDefault(kv =>
                kv.Key.EndsWith("." + c, StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith(c, StringComparison.OrdinalIgnoreCase)
            );

            if (!string.IsNullOrEmpty(hit.Key) && !string.IsNullOrWhiteSpace(hit.Value))
            {
                return Norm(hit.Value);
            }
        }

        return "";
    }

    static int PickInt(Dictionary<string, string> d, params string[] candidatesOrSuffixes)
    {
        string v = Pick(d, candidatesOrSuffixes);

        if (int.TryParse(Norm(v), out int n))
        {
            return n;
        }

        return -1;
    }

    static string PickByKeyContains(Dictionary<string, string> d, params string[] tokens)
    {
        foreach (var kv in d)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (kv.Key.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Norm(kv.Value);
                }
            }
        }

        return "";
    }

    static string PickByKeyContainsAll(Dictionary<string, string> d, params string[] tokens)
    {
        foreach (var kv in d)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            bool ok = true;

            for (int i = 0; i < tokens.Length; i++)
            {
                if (kv.Key.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return Norm(kv.Value);
            }
        }

        return "";
    }

    static bool TryExtractArrayIndex(string key, out int index)
    {
        index = -1;

        int start = key.IndexOf("[", StringComparison.Ordinal);
        if (start < 0) return false;

        int end = key.IndexOf("]", start + 1, StringComparison.Ordinal);
        if (end < 0) return false;

        string text = key.Substring(start + 1, end - start - 1);

        return int.TryParse(text, out index);
    }

    static bool TryExtractJyokenSuffixIndex(string key, out int index)
    {
        index = -1;

        string lower = key.ToLowerInvariant();

        string[] tokens =
        {
            "jyokencd",
            "jokencd",
            "jyokencode",
            "jokencode"
        };

        foreach (string token in tokens)
        {
            int pos = lower.LastIndexOf(token, StringComparison.Ordinal);
            if (pos < 0)
            {
                continue;
            }

            int numStart = pos + token.Length;

            if (numStart >= lower.Length)
            {
                continue;
            }

            var sb = new StringBuilder();

            for (int i = numStart; i < lower.Length; i++)
            {
                char c = lower[i];

                if (c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                else
                {
                    break;
                }
            }

            if (sb.Length == 0)
            {
                continue;
            }

            int n;
            if (int.TryParse(sb.ToString(), out n))
            {
                // JyokenCD5 のような1始まり表記なら、5番目を index=4 として扱う
                index = n - 1;
                return true;
            }
        }

        return false;
    }

    // =========================
    // 整形
    // =========================

    static string Csv(string s)
    {
        if (s == null)
        {
            s = "";
        }

        s = s.Replace("\r", " ").Replace("\n", " ");

        if (s == "NULL")
        {
            return "NULL";
        }

        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static string Norm(string s)
    {
        if (s == null)
        {
            return "";
        }

        return s.Trim(' ', '　');
    }

    static string ToNullLiteral(string s)
    {
        s = Norm(s);

        if (s.Length == 0)
        {
            return "NULL";
        }

        return s;
    }

    static string Pad2(string s)
    {
        s = Norm(s);

        if (s.Length == 0)
        {
            return "";
        }

        if (s.Length == 1)
        {
            return "0" + s;
        }

        return s;
    }

    static string ToIntText(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0)
        {
            return "";
        }

        if (!IsSignedInteger(raw))
        {
            string digits = ExtractSignedDigits(raw);
            if (digits.Length == 0) return "";
            raw = digits;
        }

        int n;
        if (!int.TryParse(raw, out n))
        {
            return "";
        }

        return n.ToString();
    }

    static bool IsSignedInteger(string s)
    {
        s = Norm(s);

        if (s.Length == 0) return false;

        int start = 0;

        if (s[0] == '+' || s[0] == '-')
        {
            if (s.Length == 1) return false;
            start = 1;
        }

        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') return false;
        }

        return true;
    }

    static string NormalizeCodeNumber(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";

        if (IsSignedInteger(raw))
        {
            return ToIntText(raw);
        }

        string digits = ExtractDigits(raw);
        if (digits.Length == 0) return "";

        int n;
        if (!int.TryParse(digits, out n))
        {
            return "";
        }

        return n.ToString();
    }

    static string NormalizeBodyWeight(string raw)
    {
        raw = Norm(raw);

        if (raw.Length == 0) return "";
        if (raw.IndexOf("計", StringComparison.OrdinalIgnoreCase) >= 0) return "";
        if (raw.IndexOf("不", StringComparison.OrdinalIgnoreCase) >= 0) return "";

        string digits = ExtractDigits(raw);
        if (digits.Length == 0) return "";

        if (!int.TryParse(digits, out int n)) return "";
        if (n < 200 || n > 800) return "";

        return n.ToString();
    }

    static string FormatZogenToIntText(string fugoRaw, string saRaw)
    {
        fugoRaw = Norm(fugoRaw);
        saRaw = Norm(saRaw);

        if (saRaw.Length == 0) return "";

        string digits = ExtractDigits(saRaw);
        if (digits.Length == 0) return "";

        if (!int.TryParse(digits, out int sa)) return "";
        if (sa == 999) return "";
        if (sa == 0) return "0";
        if (sa > 99) return "";

        bool minus =
            fugoRaw.IndexOf("-", StringComparison.Ordinal) >= 0 ||
            fugoRaw.IndexOf("－", StringComparison.Ordinal) >= 0;

        if (minus)
        {
            return "-" + sa.ToString();
        }

        return sa.ToString();
    }

    static string ExtractDigits(string s)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    static string ExtractSignedDigits(string s)
    {
        s = Norm(s);

        var sb = new StringBuilder();

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if ((c == '+' || c == '-') && sb.Length == 0)
            {
                sb.Append(c);
                continue;
            }

            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}