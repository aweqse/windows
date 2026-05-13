using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class Program
{
    // =========================
    // 基本設定
    // =========================

    // 4 = セットアップデータをダイアログなしで取得
    static readonly int OpenOption = 4;

    // JVGetsバッファ
    static readonly int BUFSIZE = 110000;

    // ダウンロード中 rc=-3 の待機
    static readonly int DownloadWaitMs = 5000;

    // 最大待機回数：5秒 × 720回 = 約60分
    static readonly int MaxDownloadWaitCount = 720;

    // SEHException は粘らない
    // 何度もリトライすると ContextSwitchDeadlock になりやすいので1回で停止
    static readonly int MaxSehRetryCount = 1;

    // JVOpen直後に dl > 0 の場合の初期待機
    static readonly int InitialDownloadWaitMs = 30000;

    // 取得失敗ファイル削除後の待機時間：5分
    static readonly int RestartWaitMs = 300000;

    // 失敗ファイル削除後に同じDataSpecを再実行する最大回数
    static readonly int MaxAutoRestartCount = 30;

    // 全件取得用 fromTime
    static readonly string AllFromTime = "00000000000000";

    // ALL0でJVOpenに失敗した場合の保険
    static readonly string FallbackFromTime = "19860101000000";

    // ★必ず自分のJV-Linkデータ保存先に変更すること
    // 例:
    // static readonly string JvDataRoot = @"C:\JVDataLab_full";
    // static readonly string JvDataRoot = @"C:\Users\dev-w\Desktop\JVDataLab_test";
    static readonly string JvDataRoot = @"C:\JVDataLab_full";

    // true = 失敗したと思われるJVDファイルを削除する
    // false = 削除せずログだけ出す
    static readonly bool DeleteFailedJvdFile = true;

    // 取得対象
    // RACE = RA/SEなどレース結果系
    // DIFF = 騎手/調教師マスタなど差分系
    static readonly string[] DataSpecs = new[]
    {
        "RACE",
        "DIFF"
    };

    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("JV-Link セットアップデータ一括取得専用ツール");
        Console.WriteLine("CSV出力は行いません。JVGetsで最後まで読み切ってローカル保存先に取得します。");
        Console.WriteLine("SEHException発生時は、失敗したと思われるJVDを削除して5分後に同じDataSpecを再実行します。");
        Console.WriteLine();

        Console.WriteLine("JV-Linkデータ保存先:");
        Console.WriteLine("  " + JvDataRoot);
        Console.WriteLine();

        if (!Directory.Exists(JvDataRoot))
        {
            Console.WriteLine("[WARN] JvDataRoot が存在しません。");
            Console.WriteLine("[WARN] 失敗ファイルの自動削除ができません。");
            Console.WriteLine("[WARN] JV-Link設定画面のデータ保存先に合わせて JvDataRoot を修正してください。");
            Console.WriteLine();
        }

        string[] targetSpecs;

        if (args.Length > 0)
        {
            targetSpecs = args;
        }
        else
        {
            targetSpecs = DataSpecs;
        }

        Console.WriteLine("取得対象 DataSpec:");

        foreach (string spec in targetSpecs)
        {
            Console.WriteLine("  " + spec);
        }

        Console.WriteLine();

        foreach (string spec in targetSpecs)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("開始 DataSpec=" + spec);
            Console.WriteLine("========================================");

            bool ok = DownloadSetupDataForSpecWithAutoRestart(spec);

            if (ok)
            {
                Console.WriteLine("[OK] DataSpec=" + spec + " の取得が完了しました。");
            }
            else
            {
                Console.WriteLine("[NG] DataSpec=" + spec + " の取得に失敗しました。");
                Console.WriteLine("このDataSpecで失敗したため、以降の処理は続行せず停止します。");
                break;
            }

            Console.WriteLine();

            // COMの状態を残さないため、DataSpecごとに少し待つ
            Thread.Sleep(3000);
        }

        Console.WriteLine("処理終了。Enterで閉じます。");
        Console.ReadLine();
    }

    // =========================
    // 自動削除・再実行つき取得
    // =========================

    static bool DownloadSetupDataForSpecWithAutoRestart(string dataSpec)
    {
        for (int attempt = 1; attempt <= MaxAutoRestartCount; attempt++)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("DataSpec=" + dataSpec + " attempt=" + attempt + "/" + MaxAutoRestartCount);
            Console.WriteLine("----------------------------------------");

            DownloadResult result = DownloadSetupDataForSpecOnce(dataSpec);

            if (result.Success)
            {
                return true;
            }

            Console.WriteLine("[NG] DataSpec=" + dataSpec + " の取得が失敗しました。");
            Console.WriteLine("errorMessage=" + result.ErrorMessage);
            Console.WriteLine("failedFileName=" + result.FailedFileName);
            Console.WriteLine("lastFileName=" + result.LastFileName);

            string targetFile = result.FailedFileName;

            if (string.IsNullOrWhiteSpace(targetFile))
            {
                targetFile = result.LastFileName;
            }

            if (string.IsNullOrWhiteSpace(targetFile))
            {
                Console.WriteLine("[NG] 失敗ファイル名を特定できないため、自動削除・再実行は行いません。");
                return false;
            }

            bool deleted = DeleteFailedFileIfExists(targetFile);

            if (!deleted)
            {
                Console.WriteLine("[WARN] 失敗ファイルを削除できませんでした。");
                Console.WriteLine("[WARN] 同じ場所で再度失敗する可能性があります。");
            }

            if (attempt >= MaxAutoRestartCount)
            {
                Console.WriteLine("[NG] 自動再実行回数の上限に達しました。");
                return false;
            }

            Console.WriteLine("5分待機してから DataSpec=" + dataSpec + " を再実行します。");
            WaitWithCountdown(RestartWaitMs);
        }

        return false;
    }

    static DownloadResult DownloadSetupDataForSpecOnce(string dataSpec)
    {
        dynamic jv = null;

        try
        {
            var t = Type.GetTypeFromProgID("JVDTLab.JVLink");

            if (t == null)
            {
                return DownloadResult.Fail("", "", "JV-Link(COM) が見つかりません。");
            }

            jv = Activator.CreateInstance(t);

            int initRc = jv.JVInit("SetupDownloader");
            Console.WriteLine("JVInit rc=" + initRc);

            if (initRc != 0)
            {
                return DownloadResult.Fail("", "", "JVInit失敗 rc=" + initRc);
            }

            bool opened = false;
            string usedFromTime = "";

            opened = TryOpen(jv, dataSpec, AllFromTime, out usedFromTime);

            if (!opened)
            {
                Console.WriteLine("[WARN] fromTime=00000000000000 でJVOpenできなかったため、19860101000000で再試行します。");
                opened = TryOpen(jv, dataSpec, FallbackFromTime, out usedFromTime);
            }

            if (!opened)
            {
                return DownloadResult.Fail("", "", "JVOpenできませんでした。DataSpec=" + dataSpec);
            }

            Console.WriteLine("使用 fromTime=" + usedFromTime);

            DownloadResult result = DrainJvGets(jv, dataSpec);

            try
            {
                jv.JVClose();
            }
            catch
            {
            }

            return result;
        }
        catch (Exception ex)
        {
            return DownloadResult.Fail("", "", "予期しない例外: " + ex.GetType().FullName + " / " + ex.Message);
        }
        finally
        {
            if (jv != null)
            {
                try
                {
                    jv.JVClose();
                }
                catch
                {
                }

                try
                {
                    Marshal.FinalReleaseComObject(jv);
                }
                catch
                {
                }

                jv = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }

    static bool TryOpen(dynamic jv, string dataSpec, string fromTime, out string usedFromTime)
    {
        usedFromTime = "";

        int readCount = 0;
        int downloadCount = 0;
        string lastTs = "";

        int rc;

        try
        {
            rc = jv.JVOpen(dataSpec, fromTime, OpenOption, ref readCount, ref downloadCount, ref lastTs);
        }
        catch (SEHException ex)
        {
            Console.WriteLine("[NG] JVOpenでSEHException発生。DataSpec=" + dataSpec + ", fromTime=" + fromTime);
            Console.WriteLine("message=" + ex.Message);
            return false;
        }
        catch (COMException ex)
        {
            Console.WriteLine("[NG] JVOpenでCOMException発生。DataSpec=" + dataSpec + ", fromTime=" + fromTime);
            Console.WriteLine("message=" + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NG] JVOpenで例外発生。DataSpec=" + dataSpec + ", fromTime=" + fromTime);
            Console.WriteLine("type=" + ex.GetType().FullName);
            Console.WriteLine("message=" + ex.Message);
            return false;
        }

        Console.WriteLine("JVOpen(" + dataSpec + ") rc=" + rc + ", read=" + readCount + ", dl=" + downloadCount + ", lastTs=" + lastTs);

        if (rc != 0)
        {
            return false;
        }

        usedFromTime = fromTime;

        if (downloadCount > 0)
        {
            Console.WriteLine(dataSpec + " にダウンロード対象があります。少し待機します。dl=" + downloadCount);
            Thread.Sleep(InitialDownloadWaitMs);
        }

        return true;
    }

    static DownloadResult DrainJvGets(dynamic jv, string dataSpec)
    {
        object objBuff = new byte[BUFSIZE];
        var sjis = Encoding.GetEncoding("shift_jis");

        int totalRecordCount = 0;
        int fileSwitchCount = 0;
        int downloadWaitCount = 0;
        int sehRetryCount = 0;

        var recIdCount = new Dictionary<string, int>(StringComparer.Ordinal);

        string lastFileName = "";
        string currentFileName = "";

        while (true)
        {
            int rc;
            string fileName = "";

            try
            {
                rc = jv.JVGets(ref objBuff, BUFSIZE, out fileName);
            }
            catch (SEHException ex)
            {
                sehRetryCount++;

                string failedFileName = currentFileName;

                if (string.IsNullOrWhiteSpace(failedFileName))
                {
                    failedFileName = lastFileName;
                }

                Console.WriteLine("[NG] JVGets(" + dataSpec + ")でSEHException発生 retry=" + sehRetryCount);
                Console.WriteLine("message=" + ex.Message);
                Console.WriteLine("currentFileName=" + currentFileName);
                Console.WriteLine("lastFileName=" + lastFileName);
                Console.WriteLine("failedFileName=" + failedFileName);

                if (sehRetryCount >= MaxSehRetryCount)
                {
                    Console.WriteLine("[NG] SEHExceptionの上限に達したため、このDataSpecを一旦停止します。");
                    Console.WriteLine("[HINT] failedFileName付近のJVDが壊れている可能性があります。");

                    return DownloadResult.Fail(failedFileName, lastFileName, ex.Message);
                }

                objBuff = new byte[BUFSIZE];
                Thread.Sleep(DownloadWaitMs);
                continue;
            }
            catch (COMException ex)
            {
                string failedFileName = currentFileName;

                if (string.IsNullOrWhiteSpace(failedFileName))
                {
                    failedFileName = lastFileName;
                }

                Console.WriteLine("[NG] JVGets(" + dataSpec + ")でCOMException発生");
                Console.WriteLine("message=" + ex.Message);
                Console.WriteLine("currentFileName=" + currentFileName);
                Console.WriteLine("lastFileName=" + lastFileName);

                return DownloadResult.Fail(failedFileName, lastFileName, ex.Message);
            }
            catch (Exception ex)
            {
                string failedFileName = currentFileName;

                if (string.IsNullOrWhiteSpace(failedFileName))
                {
                    failedFileName = lastFileName;
                }

                Console.WriteLine("[NG] JVGets(" + dataSpec + ")で例外発生");
                Console.WriteLine("type=" + ex.GetType().FullName);
                Console.WriteLine("message=" + ex.Message);
                Console.WriteLine("currentFileName=" + currentFileName);
                Console.WriteLine("lastFileName=" + lastFileName);

                return DownloadResult.Fail(failedFileName, lastFileName, ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                lastFileName = fileName;
            }

            if (rc == 0)
            {
                Console.WriteLine("JVGets(" + dataSpec + ") rc=0 終端に到達しました。");
                break;
            }

            if (rc == -1)
            {
                fileSwitchCount++;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    currentFileName = fileName;
                    lastFileName = fileName;
                }

                Console.WriteLine("JVGets(" + dataSpec + ") ファイル切替 rc=-1 count=" + fileSwitchCount + " file=" + fileName);

                continue;
            }

            if (rc == -3)
            {
                downloadWaitCount++;

                Console.WriteLine("JVGets(" + dataSpec + ") ダウンロード中 rc=-3 wait=" + downloadWaitCount + " file=" + fileName);

                if (downloadWaitCount >= MaxDownloadWaitCount)
                {
                    Console.WriteLine("[NG] ダウンロード待機が上限に達しました。");
                    Console.WriteLine("currentFileName=" + currentFileName);
                    Console.WriteLine("lastFileName=" + lastFileName);

                    return DownloadResult.Fail(currentFileName, lastFileName, "ダウンロード待機が上限に達しました。");
                }

                Thread.Sleep(DownloadWaitMs);
                continue;
            }

            if (rc < 0)
            {
                Console.WriteLine("[NG] JVGets(" + dataSpec + ") エラー rc=" + rc + " file=" + fileName);
                Console.WriteLine("currentFileName=" + currentFileName);
                Console.WriteLine("lastFileName=" + lastFileName);

                return DownloadResult.Fail(currentFileName, lastFileName, "JVGets rc=" + rc);
            }

            downloadWaitCount = 0;
            sehRetryCount = 0;

            totalRecordCount++;

            if (totalRecordCount % 10000 == 0)
            {
                Console.WriteLine("read records=" + totalRecordCount + " currentFile=" + currentFileName);
            }

            byte[] bytes = objBuff as byte[];

            if (bytes != null && rc >= 2 && rc <= bytes.Length)
            {
                string str = "";

                try
                {
                    str = sjis.GetString(bytes, 0, rc);
                }
                catch
                {
                    str = "";
                }

                if (str.Length >= 2)
                {
                    string recId = str.Substring(0, 2);

                    if (!recIdCount.ContainsKey(recId))
                    {
                        recIdCount[recId] = 0;
                    }

                    recIdCount[recId]++;
                }
            }
        }

        Console.WriteLine("DataSpec=" + dataSpec + " 読み切り完了");
        Console.WriteLine("totalRecordCount=" + totalRecordCount);
        Console.WriteLine("fileSwitchCount=" + fileSwitchCount);
        Console.WriteLine("lastFileName=" + lastFileName);

        Console.WriteLine("Record ID count:");

        foreach (var kv in recIdCount.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("  " + kv.Key + " = " + kv.Value);
        }

        return DownloadResult.Ok(lastFileName);
    }

    // =========================
    // 失敗ファイル削除
    // =========================

    static bool DeleteFailedFileIfExists(string failedFileName)
    {
        failedFileName = NormalizeFileName(failedFileName);

        if (string.IsNullOrWhiteSpace(failedFileName))
        {
            Console.WriteLine("[WARN] 削除対象ファイル名が空です。");
            return false;
        }

        if (!DeleteFailedJvdFile)
        {
            Console.WriteLine("[INFO] DeleteFailedJvdFile=false のため削除しません。target=" + failedFileName);
            return true;
        }

        if (!Directory.Exists(JvDataRoot))
        {
            Console.WriteLine("[WARN] JvDataRoot が存在しません。削除できません。");
            Console.WriteLine("JvDataRoot=" + JvDataRoot);
            return false;
        }

        Console.WriteLine("失敗ファイルを検索します。target=" + failedFileName);
        Console.WriteLine("searchRoot=" + JvDataRoot);

        List<string> targets = new List<string>();

        try
        {
            if (Path.IsPathRooted(failedFileName) && File.Exists(failedFileName))
            {
                targets.Add(failedFileName);
            }
            else
            {
                string justName = Path.GetFileName(failedFileName);

                if (string.IsNullOrWhiteSpace(justName))
                {
                    justName = failedFileName;
                }

                targets = Directory
                    .GetFiles(JvDataRoot, justName, SearchOption.AllDirectories)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[WARN] ファイル検索中に例外が発生しました。");
            Console.WriteLine("message=" + ex.Message);
            return false;
        }

        if (targets.Count == 0)
        {
            Console.WriteLine("[WARN] 削除対象ファイルが見つかりませんでした。target=" + failedFileName);
            return false;
        }

        bool allDeleted = true;

        foreach (string path in targets)
        {
            Console.WriteLine("削除対象: " + path);

            bool deleted = DeleteFileWithRetry(path);

            if (!deleted)
            {
                allDeleted = false;
            }
        }

        return allDeleted;
    }

    static bool DeleteFileWithRetry(string path)
    {
        for (int i = 1; i <= 5; i++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("[INFO] 既に存在しません: " + path);
                    return true;
                }

                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);

                Console.WriteLine("[OK] 削除しました: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] ファイル削除失敗 retry=" + i);
                Console.WriteLine("path=" + path);
                Console.WriteLine("message=" + ex.Message);

                Thread.Sleep(3000);
            }
        }

        Console.WriteLine("[NG] ファイル削除に失敗しました: " + path);
        return false;
    }

    static string NormalizeFileName(string name)
    {
        if (name == null)
        {
            return "";
        }

        return name.Trim(' ', '　', '\t', '\r', '\n');
    }

    // =========================
    // 待機表示
    // =========================

    static void WaitWithCountdown(int waitMs)
    {
        int totalSeconds = waitMs / 1000;

        while (totalSeconds > 0)
        {
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            Console.WriteLine("再実行まで残り " + minutes.ToString("00") + ":" + seconds.ToString("00"));

            int sleep = Math.Min(30, totalSeconds);
            Thread.Sleep(sleep * 1000);
            totalSeconds -= sleep;
        }
    }

    // =========================
    // 結果クラス
    // =========================

    class DownloadResult
    {
        public bool Success;
        public string FailedFileName;
        public string LastFileName;
        public string ErrorMessage;

        public static DownloadResult Ok(string lastFileName)
        {
            return new DownloadResult
            {
                Success = true,
                FailedFileName = "",
                LastFileName = lastFileName ?? "",
                ErrorMessage = ""
            };
        }

        public static DownloadResult Fail(string failedFileName, string lastFileName, string errorMessage)
        {
            return new DownloadResult
            {
                Success = false,
                FailedFileName = failedFileName ?? "",
                LastFileName = lastFileName ?? "",
                ErrorMessage = errorMessage ?? ""
            };
        }
    }
}