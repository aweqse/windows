using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static JVData_Struct;

namespace race_schedule
{
    // ============================================================
    // ここだけ変更すればOK：本文固定設定
    // ============================================================
    internal static class AppConfig
    {
        // 出力先フォルダ
        // ログ・CSV・race_keys.txt・raw はすべてこの配下に出力する。
        public const string OutputDir = @"C:\Users\dev-w\Desktop\workspace\output\log\race_schedule_log";

        // JVInit に渡すSID
        public const string Sid = "UNKNOWN";

        // JVOpenのオプション
        // 4 = セットアップデータをダイアログなしで取得
        public const int OpenOption = 4;

        // RACEデータ取得開始日を実行日から何日前に戻すか
        // 出力対象日は実行日に固定したまま、取得範囲だけ広げる。
        public const int RaceOpenLookbackDays = 14;

        // JVGetsバッファサイズ
        public const int BufferSize = 110000;

        // rc=-3などの待機ms
        public const int ReadWaitMs = 3000;

        // JVOpen後にダウンロード対象がある場合の初期待機
        public const int InitialDownloadWaitMs = 30000;

        // SEHExceptionの最大リトライ回数
        public const int MaxSehRetryCount = 10;
    }

    internal class Program
    {
        /*
         * race_schedule
         *
         * JV-Link RACEデータから「実行日」の通常発走予定時刻を取得する専用exe。
         *
         * 方針：
         *   出力ファイル名       = 実行日 yyyyMMdd
         *   CSV出力対象         = 実行日のRAレコードのみ
         *   race_keys出力対象    = 実行日のRAレコードのみ
         *   JVOpen取得開始日時  = 実行日 - AppConfig.RaceOpenLookbackDays 日
         *
         * 目的：
         *   通常の発走時刻を取得する。
         *   30分前 / 10分前 / 5分前のオッズ取得判定時刻を作る。
         *   0B31オッズ取得に使う race_key 候補も出力する。
         *
         * 出力：
         *   start_race_time      = HH:mm:ss
         *   start_race_datetime  = yyyy-MM-dd HH:mm:ss
         *   m30_target_time      = HH:mm:ss
         *   m10_target_time      = HH:mm:ss
         *   m5_target_time       = HH:mm:ss
         *
         * 取得元：
         *   DataSpec = RACE
         *   RAレコードの HassoTime を通常発走時刻として使用する。
         *
         * 出力例：
         *   C:\Users\dev-w\Desktop\workspace\output\race_schedule_log\20260523_race_schedule.log
         *   C:\Users\dev-w\Desktop\workspace\output\race_schedule_log\20260523_race_schedule_manifest.txt
         *   C:\Users\dev-w\Desktop\workspace\output\race_schedule_log\20260523_race_schedule.csv
         *   C:\Users\dev-w\Desktop\workspace\output\race_schedule_log\20260523_race_keys.txt
         *   C:\Users\dev-w\Desktop\workspace\output\race_schedule_log\20260523_RACE_RA.raw.txt
         *
         * race_key：
         *   YYYYMMDD + JyoCD + Kaiji + Nichiji + RaceNum
         *   例：2026052304010701
         *
         * race_id：
         *   YYYY + JyoCD + Kaiji + Nichiji + RaceNum
         *   例：202605010101
         *
         * 注意：
         *   Visual Studioでは必ず x86 でビルドすること。
         *   JVData_Struct.cs をプロジェクトに追加すること。
         *   JV-Link COMは32bit前提。
         */

        private const string MutexName = @"Global\KeibaRaceScheduleGetterMutex";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("============================================================");
            Console.WriteLine("race_schedule START");
            Console.WriteLine("============================================================");
            Console.WriteLine("実行時引数:");

            if (args == null || args.Length == 0)
            {
                Console.WriteLine("(なし。本文固定設定で実行します)");
            }
            else
            {
                Console.WriteLine(string.Join(" ", args));
            }

            Console.WriteLine("============================================================");

            RunOptions options;

            try
            {
                options = RunOptions.Parse(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ARG ERROR] " + ex.Message);
                PrintUsage();
                return 2;
            }

            using (var mutex = new Mutex(false, MutexName))
            {
                bool hasHandle = false;

                try
                {
                    try
                    {
                        hasHandle = mutex.WaitOne(TimeSpan.FromSeconds(1), false);
                    }
                    catch (AbandonedMutexException)
                    {
                        hasHandle = true;
                    }

                    if (!hasHandle)
                    {
                        Console.WriteLine("[SKIP] 既に通常発走時刻取得プログラムが実行中です。JV-Link同時実行防止のため終了します。");
                        return 10;
                    }

                    var runner = new RaceScheduleRunner(options);
                    return runner.Run();
                }
                finally
                {
                    if (hasHandle)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  race_schedule.exe");
            Console.WriteLine();
            Console.WriteLine("Optional:");
            Console.WriteLine("  race_schedule.exe --output-dir PATH");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output-dir PATH                任意。省略時はAppConfig.OutputDir。");
            Console.WriteLine("  --sid SID                        任意。JVInitに渡すSID。省略時はAppConfig.Sid。");
            Console.WriteLine("  --open-option N                  任意。JVOpenのオプション。省略時はAppConfig.OpenOption。");
            Console.WriteLine("  --buffer-size N                  任意。JVGetsバッファサイズ。省略時はAppConfig.BufferSize。");
            Console.WriteLine("  --read-wait-ms N                 任意。rc=-3時の待機ms。省略時はAppConfig.ReadWaitMs。");
            Console.WriteLine();
            Console.WriteLine("Note:");
            Console.WriteLine("  出力対象日は常にexe実行日です。--date指定は使いません。");
            Console.WriteLine("  JVOpenの取得開始日時だけ、実行日からAppConfig.RaceOpenLookbackDays日前に戻します。");
            Console.WriteLine();
        }
    }

    internal sealed class RunOptions
    {
        public string ExecutionDateYmd { get; private set; }
        public string OutputDir { get; private set; }
        public string Sid { get; private set; }
        public int OpenOption { get; private set; }
        public int BufferSize { get; private set; }
        public int ReadWaitMs { get; private set; }

        private RunOptions()
        {
            ExecutionDateYmd = DateTime.Now.ToString("yyyyMMdd");
            OutputDir = AppConfig.OutputDir;
            Sid = AppConfig.Sid;
            OpenOption = AppConfig.OpenOption;
            BufferSize = AppConfig.BufferSize;
            ReadWaitMs = AppConfig.ReadWaitMs;
        }

        public static RunOptions Parse(string[] args)
        {
            var opt = new RunOptions();
            var dict = ParseArgs(args);

            if (dict.TryGetValue("--output-dir", out string outputDir) && !string.IsNullOrWhiteSpace(outputDir))
            {
                opt.OutputDir = outputDir.Trim();
            }

            if (dict.TryGetValue("--sid", out string sid))
            {
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    opt.Sid = sid.Trim();
                }
            }

            if (dict.TryGetValue("--open-option", out string openOptionText))
            {
                if (!int.TryParse(openOptionText, out int openOption))
                {
                    throw new Exception("--open-option は数値で指定してください。");
                }

                opt.OpenOption = openOption;
            }

            if (dict.TryGetValue("--buffer-size", out string bufferText))
            {
                if (!int.TryParse(bufferText, out int bufferSize))
                {
                    throw new Exception("--buffer-size は数値で指定してください。");
                }

                opt.BufferSize = Math.Max(1024, bufferSize);
            }

            if (dict.TryGetValue("--read-wait-ms", out string waitText))
            {
                if (!int.TryParse(waitText, out int waitMs))
                {
                    throw new Exception("--read-wait-ms は数値で指定してください。");
                }

                opt.ReadWaitMs = Math.Max(0, waitMs);
            }

            return opt;
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (args == null || args.Length == 0)
            {
                return dict;
            }

            int i = 0;

            while (i < args.Length)
            {
                string key = args[i];

                if (!key.StartsWith("--"))
                {
                    throw new Exception("不明な引数です: " + key);
                }

                if (string.Equals(key, "--date", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("--date は廃止しました。出力対象日は常にexe実行日です。");
                }

                if (i + 1 >= args.Length)
                {
                    throw new Exception("値が指定されていません: " + key);
                }

                string value = args[i + 1];
                dict[key] = value;
                i += 2;
            }

            return dict;
        }
    }

    internal sealed class RaceScheduleRunner
    {
        private const string DataSpec = "RACE";

        private readonly RunOptions _options;
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);
        private readonly Encoding _sjis = Encoding.GetEncoding("shift_jis");

        private string _outputDir;
        private string _fileDateBase;

        private Logger _logger;
        private ManifestWriter _manifest;

        private readonly List<RaceScheduleRecord> _records = new List<RaceScheduleRecord>();

        public RaceScheduleRunner(RunOptions options)
        {
            _options = options;
        }

        public int Run()
        {
            DateTime startedAt = DateTime.Now;

            try
            {
                PrepareOutputDirectory();

                string logPath = Path.Combine(_outputDir, _fileDateBase + "_race_schedule.log");
                string manifestPath = Path.Combine(_outputDir, _fileDateBase + "_race_schedule_manifest.txt");

                _logger = new Logger(logPath);
                _manifest = new ManifestWriter(manifestPath);

                WriteStartLog(startedAt, logPath, manifestPath);

                using (var jv = new JvLinkClient(_logger))
                {
                    int initRc = jv.Init(_options.Sid);

                    _logger.Info("JVInit rc = " + initRc);
                    _manifest.WriteLine("JVInit_rc=" + initRc);

                    if (initRc < 0)
                    {
                        _logger.Error("JVInit failed. rc=" + initRc);
                        _manifest.WriteLine("status=failed");
                        _manifest.WriteLine("error=JVInit failed. rc=" + initRc);
                        return 20;
                    }

                    FetchResult result = FetchRaceSchedule(jv);

                    try
                    {
                        jv.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("JVCloseで例外: " + ex.Message);
                    }

                    string scheduleCsvPath = WriteScheduleCsv();
                    string raceKeysPath = WriteRaceKeysTxt();

                    DateTime finishedAt = DateTime.Now;

                    _logger.Info("");
                    _logger.Info("schedule_csv      = " + scheduleCsvPath);
                    _logger.Info("race_keys_txt     = " + raceKeysPath);
                    _logger.Info("schedule_count    = " + _records.Count);
                    _logger.Info("finished_at       = " + finishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    _logger.Info("elapsed_seconds   = " + (finishedAt - startedAt).TotalSeconds.ToString("0.000"));

                    _manifest.WriteLine("");
                    _manifest.WriteLine("schedule_csv=" + scheduleCsvPath);
                    _manifest.WriteLine("race_keys_txt=" + raceKeysPath);
                    _manifest.WriteLine("schedule_count=" + _records.Count);
                    _manifest.WriteLine("finished_at=" + finishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    _manifest.WriteLine("elapsed_seconds=" + (finishedAt - startedAt).TotalSeconds.ToString("0.000"));

                    if (result == FetchResult.Success)
                    {
                        _logger.Info("overall_status    = done");
                        _manifest.WriteLine("status=done");

                        _logger.Info("============================================================");
                        _logger.Info("race_schedule END");
                        _logger.Info("============================================================");

                        return 0;
                    }

                    if (result == FetchResult.DoneButEmpty)
                    {
                        _logger.Warn("overall_status    = done_but_empty");
                        _manifest.WriteLine("status=done_but_empty");

                        _logger.Info("============================================================");
                        _logger.Info("race_schedule END");
                        _logger.Info("============================================================");

                        return 31;
                    }

                    _logger.Error("overall_status    = failed");
                    _manifest.WriteLine("status=failed");

                    _logger.Info("============================================================");
                    _logger.Info("race_schedule END");
                    _logger.Info("============================================================");

                    return 30;
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.Error("Fatal error: " + ex);
                }
                else
                {
                    Console.WriteLine("[FATAL] " + ex);
                }

                if (_manifest != null)
                {
                    _manifest.WriteLine("status=failed");
                    _manifest.WriteLine("error=" + ex.Message);
                }

                return 99;
            }
            finally
            {
                if (_logger != null)
                {
                    _logger.Dispose();
                }

                if (_manifest != null)
                {
                    _manifest.Dispose();
                }
            }
        }

        private void PrepareOutputDirectory()
        {
            _fileDateBase = _options.ExecutionDateYmd;
            _outputDir = Path.GetFullPath(_options.OutputDir);
            Directory.CreateDirectory(_outputDir);
        }

        private void WriteStartLog(DateTime startedAt, string logPath, string manifestPath)
        {
            DateTime executionDate = ParseYmd(_options.ExecutionDateYmd);
            DateTime openFromDate = executionDate.AddDays(-AppConfig.RaceOpenLookbackDays);
            string openFromTime = openFromDate.ToString("yyyyMMdd") + "000000";

            _logger.Info("============================================================");
            _logger.Info("race_schedule START");
            _logger.Info("============================================================");
            _logger.Info("started_at        = " + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _logger.Info("execution_date    = " + _options.ExecutionDateYmd);
            _logger.Info("target_date       = " + _options.ExecutionDateYmd);
            _logger.Info("file_date_base    = " + _fileDateBase);
            _logger.Info("data_spec         = " + DataSpec);
            _logger.Info("output_dir        = " + _outputDir);
            _logger.Info("log_path          = " + logPath);
            _logger.Info("manifest_path     = " + manifestPath);
            _logger.Info("sid               = " + MaskSid(_options.Sid));
            _logger.Info("sid_empty         = " + string.IsNullOrEmpty(_options.Sid));
            _logger.Info("open_option       = " + _options.OpenOption);
            _logger.Info("open_lookback_days= " + AppConfig.RaceOpenLookbackDays);
            _logger.Info("open_from_time    = " + openFromTime);
            _logger.Info("buffer_size       = " + _options.BufferSize);
            _logger.Info("read_wait_ms      = " + _options.ReadWaitMs);
            _logger.Info("");

            _manifest.WriteLine("started_at=" + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _manifest.WriteLine("execution_date=" + _options.ExecutionDateYmd);
            _manifest.WriteLine("target_date=" + _options.ExecutionDateYmd);
            _manifest.WriteLine("file_date_base=" + _fileDateBase);
            _manifest.WriteLine("data_spec=" + DataSpec);
            _manifest.WriteLine("output_dir=" + _outputDir);
            _manifest.WriteLine("log_path=" + logPath);
            _manifest.WriteLine("manifest_path=" + manifestPath);
            _manifest.WriteLine("sid=" + MaskSid(_options.Sid));
            _manifest.WriteLine("sid_empty=" + string.IsNullOrEmpty(_options.Sid));
            _manifest.WriteLine("open_option=" + _options.OpenOption);
            _manifest.WriteLine("open_lookback_days=" + AppConfig.RaceOpenLookbackDays);
            _manifest.WriteLine("open_from_time=" + openFromTime);
            _manifest.WriteLine("buffer_size=" + _options.BufferSize);
            _manifest.WriteLine("read_wait_ms=" + _options.ReadWaitMs);
            _manifest.WriteLine("");
        }

        private FetchResult FetchRaceSchedule(JvLinkClient jv)
        {
            int readCount = 0;
            int downloadCount = 0;
            string lastTs = "";

            DateTime executionDate = ParseYmd(_options.ExecutionDateYmd);
            DateTime openFromDate = executionDate.AddDays(-AppConfig.RaceOpenLookbackDays);
            string fromTime = openFromDate.ToString("yyyyMMdd") + "000000";

            int openRc;

            try
            {
                openRc = jv.Open(DataSpec, fromTime, _options.OpenOption, ref readCount, ref downloadCount, ref lastTs);
            }
            catch (Exception ex)
            {
                _logger.Error("JVOpen exception. dataSpec=" + DataSpec + ", fromTime=" + fromTime + ", error=" + ex);
                _manifest.WriteLine("JVOpen_exception=" + ex.Message);
                return FetchResult.Failed;
            }

            _logger.Info("JVOpen dataSpec    = " + DataSpec);
            _logger.Info("JVOpen fromTime    = " + fromTime);
            _logger.Info("JVOpen option      = " + _options.OpenOption);
            _logger.Info("JVOpen rc          = " + openRc);
            _logger.Info("JVOpen read        = " + readCount);
            _logger.Info("JVOpen download    = " + downloadCount);
            _logger.Info("JVOpen lastTs      = " + lastTs);

            _manifest.WriteLine("JVOpen_dataSpec=" + DataSpec);
            _manifest.WriteLine("JVOpen_fromTime=" + fromTime);
            _manifest.WriteLine("JVOpen_option=" + _options.OpenOption);
            _manifest.WriteLine("JVOpen_rc=" + openRc);
            _manifest.WriteLine("JVOpen_read=" + readCount);
            _manifest.WriteLine("JVOpen_download=" + downloadCount);
            _manifest.WriteLine("JVOpen_lastTs=" + lastTs);

            if (openRc < 0)
            {
                _logger.Error("JVOpen failed. rc=" + openRc);
                _manifest.WriteLine("status=failed_jvopen");
                return FetchResult.Failed;
            }

            if (downloadCount > 0)
            {
                _logger.Info("downloadCount > 0 のため少し待機します。downloadCount=" + downloadCount);
                Thread.Sleep(AppConfig.InitialDownloadWaitMs);
            }

            string rawPath = Path.Combine(_outputDir, _fileDateBase + "_RACE_RA.raw.txt");

            int raCount = 0;
            int seCount = 0;
            int otherCount = 0;
            int outputCount = 0;
            int skippedByDateCount = 0;
            int skippedParseCount = 0;
            int fileSwitchCount = 0;
            int downloadWaitCount = 0;
            int sehRetryCount = 0;

            var ra = new JV_RA_RACE();

            object objBuff = Array.Empty<byte>();

            using (var rawWriter = new StreamWriter(rawPath, false, _utf8NoBom))
            {
                while (true)
                {
                    int getsRc;
                    string fileName = "";

                    try
                    {
                        getsRc = jv.Gets(ref objBuff, _options.BufferSize, out fileName);
                    }
                    catch (SEHException ex)
                    {
                        sehRetryCount++;

                        _logger.Warn("JVGetsでSEHExceptionが発生。retry=" + sehRetryCount + " message=" + ex.Message);

                        if (sehRetryCount >= AppConfig.MaxSehRetryCount)
                        {
                            _logger.Error("JVGets SEHException リトライ上限。停止します。");
                            break;
                        }

                        Thread.Sleep(_options.ReadWaitMs);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("JVGets exception. error=" + ex);
                        break;
                    }

                    if (getsRc == 0)
                    {
                        _logger.Info("JVGets rc=0. 読み込み終了。");
                        break;
                    }

                    if (getsRc == -1)
                    {
                        fileSwitchCount++;
                        _logger.Info("JVGets rc=-1. ファイル切替。count=" + fileSwitchCount);
                        continue;
                    }

                    if (getsRc == -3)
                    {
                        downloadWaitCount++;
                        _logger.Warn("JVGets rc=-3. ダウンロード中。wait_count=" + downloadWaitCount);
                        Thread.Sleep(_options.ReadWaitMs);
                        continue;
                    }

                    if (getsRc < 0)
                    {
                        _logger.Error("JVGets error. rc=" + getsRc);
                        break;
                    }

                    sehRetryCount = 0;

                    var bytes = (byte[])objBuff;

                    string text = (getsRc > 0 && getsRc <= bytes.Length)
                        ? _sjis.GetString(bytes, 0, getsRc)
                        : _sjis.GetString(bytes);

                    if (text.Length < 2)
                    {
                        otherCount++;
                        continue;
                    }

                    string recId = text.Substring(0, 2);

                    if (recId == "RA")
                    {
                        raCount++;
                        rawWriter.WriteLine(text);

                        try
                        {
                            ra.SetDataB(ref text);
                        }
                        catch (Exception ex)
                        {
                            skippedParseCount++;
                            _logger.Warn("RA SetDataB failed. file=" + fileName + " error=" + ex.Message);
                            continue;
                        }

                        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                        FlattenObject(ra, "", dict, new HashSet<int>());

                        RaceScheduleRecord record;
                        RaceScheduleCreateResult createResult = RaceScheduleRecord.TryCreate(
                            dict,
                            _options.ExecutionDateYmd,
                            out record
                        );

                        if (createResult == RaceScheduleCreateResult.Success)
                        {
                            _records.Add(record);
                            outputCount++;

                            if (outputCount <= 20)
                            {
                                _logger.Info("RA parsed #" + outputCount +
                                             " race_key=" + record.RaceKey +
                                             " race_id=" + record.RaceId +
                                             " start=" + record.StartRaceDateTimeText +
                                             " m30=" + record.M30TargetTimeText +
                                             " race_name=" + record.RaceName);
                            }
                        }
                        else if (createResult == RaceScheduleCreateResult.SkipByDate)
                        {
                            skippedByDateCount++;
                        }
                        else
                        {
                            skippedParseCount++;
                            _logger.Warn("RA parse skipped. reason=" + createResult + " head=" + SafeHead(text, 120));
                        }

                        continue;
                    }

                    if (recId == "SE")
                    {
                        seCount++;
                        continue;
                    }

                    otherCount++;
                }
            }

            try
            {
                jv.Close();
            }
            catch (Exception ex)
            {
                _logger.Warn("JVClose exception after RACE. " + ex.Message);
            }

            _records.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.RaceDate, b.RaceDate);
                if (c != 0) return c;

                c = string.CompareOrdinal(a.JyoCd, b.JyoCd);
                if (c != 0) return c;

                c = string.CompareOrdinal(a.Kaiji, b.Kaiji);
                if (c != 0) return c;

                c = string.CompareOrdinal(a.Nichiji, b.Nichiji);
                if (c != 0) return c;

                return string.CompareOrdinal(a.RaceNum, b.RaceNum);
            });

            _logger.Info("RACE finished.");
            _logger.Info("raw_path             = " + rawPath);
            _logger.Info("ra_count             = " + raCount);
            _logger.Info("se_count             = " + seCount);
            _logger.Info("other_count          = " + otherCount);
            _logger.Info("schedule_count       = " + _records.Count);
            _logger.Info("skipped_by_date      = " + skippedByDateCount);
            _logger.Info("skipped_parse        = " + skippedParseCount);
            _logger.Info("file_switch_count    = " + fileSwitchCount);
            _logger.Info("download_wait_count  = " + downloadWaitCount);

            _manifest.WriteLine("raw_path=" + rawPath);
            _manifest.WriteLine("ra_count=" + raCount);
            _manifest.WriteLine("se_count=" + seCount);
            _manifest.WriteLine("other_count=" + otherCount);
            _manifest.WriteLine("schedule_count=" + _records.Count);
            _manifest.WriteLine("skipped_by_date=" + skippedByDateCount);
            _manifest.WriteLine("skipped_parse=" + skippedParseCount);
            _manifest.WriteLine("file_switch_count=" + fileSwitchCount);
            _manifest.WriteLine("download_wait_count=" + downloadWaitCount);

            if (_records.Count > 0)
            {
                return FetchResult.Success;
            }

            return FetchResult.DoneButEmpty;
        }

        private string WriteScheduleCsv()
        {
            string csvPath = Path.Combine(_outputDir, _fileDateBase + "_race_schedule.csv");

            using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv("race_date"),
                    Csv("year"),
                    Csv("month"),
                    Csv("day"),
                    Csv("jyo_cd"),
                    Csv("kaiji"),
                    Csv("nichiji"),
                    Csv("race_num"),
                    Csv("race_key"),
                    Csv("race_id"),
                    Csv("race_name"),
                    Csv("start_race_time"),
                    Csv("start_race_datetime"),
                    Csv("m30_target_time"),
                    Csv("m10_target_time"),
                    Csv("m5_target_time"),
                    Csv("entry"),
                    Csv("course_distance"),
                    Csv("track"),
                    Csv("raw_hasso_time")
                }));

                foreach (var r in _records)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.RaceDate),
                        Csv(r.Year),
                        Csv(r.Month),
                        Csv(r.Day),
                        Csv(r.JyoCd),
                        Csv(r.Kaiji),
                        Csv(r.Nichiji),
                        Csv(r.RaceNum),
                        Csv(r.RaceKey),
                        Csv(r.RaceId),
                        Csv(r.RaceName),
                        Csv(r.StartRaceTimeText),
                        Csv(r.StartRaceDateTimeText),
                        Csv(r.M30TargetTimeText),
                        Csv(r.M10TargetTimeText),
                        Csv(r.M5TargetTimeText),
                        Csv(r.Entry),
                        Csv(r.CourseDistance),
                        Csv(r.Track),
                        Csv(r.RawHassoTime)
                    }));
                }
            }

            return csvPath;
        }

        private string WriteRaceKeysTxt()
        {
            string txtPath = Path.Combine(_outputDir, _fileDateBase + "_race_keys.txt");

            using (var writer = new StreamWriter(txtPath, false, _utf8NoBom))
            {
                foreach (var r in _records)
                {
                    if (!string.IsNullOrWhiteSpace(r.RaceKey))
                    {
                        writer.WriteLine(r.RaceKey);
                    }
                }
            }

            return txtPath;
        }

        private static void FlattenObject(object obj, string prefix, Dictionary<string, string> dict, HashSet<int> visited)
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

        private static bool IsLeafType(Type t)
        {
            return t.IsPrimitive
                || t.IsEnum
                || t == typeof(string)
                || t == typeof(decimal)
                || t == typeof(DateTime)
                || t == typeof(Guid);
        }

        private static string TrimDot(string s)
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

        private static DateTime ParseYmd(string ymd)
        {
            return DateTime.ParseExact(
                ymd,
                "yyyyMMdd",
                CultureInfo.InvariantCulture
            );
        }

        private static string Csv(string s)
        {
            if (s == null)
            {
                s = "";
            }

            s = s.Replace("\r", " ").Replace("\n", " ");

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string SafeHead(string text, int len)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string oneLine = text.Replace("\r", "\\r").Replace("\n", "\\n");

            if (oneLine.Length <= len)
            {
                return oneLine;
            }

            return oneLine.Substring(0, len);
        }

        private static string MaskSid(string sid)
        {
            if (string.IsNullOrEmpty(sid))
            {
                return "";
            }

            if (sid.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return "UNKNOWN";
            }

            if (sid.Length <= 4)
            {
                return "****";
            }

            return sid.Substring(0, 2) + "****" + sid.Substring(sid.Length - 2);
        }
    }

    internal enum RaceScheduleCreateResult
    {
        Success,
        SkipByDate,
        InvalidDate,
        InvalidRaceKey,
        InvalidStartTime,
        InvalidPlace
    }

    internal sealed class RaceScheduleRecord
    {
        public string RaceDate { get; private set; }
        public string Year { get; private set; }
        public string Month { get; private set; }
        public string Day { get; private set; }

        public string JyoCd { get; private set; }
        public string Kaiji { get; private set; }
        public string Nichiji { get; private set; }
        public string RaceNum { get; private set; }

        public string RaceKey { get; private set; }
        public string RaceId { get; private set; }

        public string RaceName { get; private set; }

        public string RawHassoTime { get; private set; }
        public string StartRaceTimeText { get; private set; }
        public string StartRaceDateTimeText { get; private set; }

        public string M30TargetTimeText { get; private set; }
        public string M10TargetTimeText { get; private set; }
        public string M5TargetTimeText { get; private set; }

        public string Entry { get; private set; }
        public string CourseDistance { get; private set; }
        public string Track { get; private set; }

        private RaceScheduleRecord()
        {
            RaceDate = "";
            Year = "";
            Month = "";
            Day = "";
            JyoCd = "";
            Kaiji = "";
            Nichiji = "";
            RaceNum = "";
            RaceKey = "";
            RaceId = "";
            RaceName = "";
            RawHassoTime = "";
            StartRaceTimeText = "";
            StartRaceDateTimeText = "";
            M30TargetTimeText = "";
            M10TargetTimeText = "";
            M5TargetTimeText = "";
            Entry = "";
            CourseDistance = "";
            Track = "";
        }

        public static RaceScheduleCreateResult TryCreate(
            Dictionary<string, string> d,
            string executionDateYmd,
            out RaceScheduleRecord record
        )
        {
            record = null;

            string year = Pick(d, "Year");
            string monthDay = Pick(d, "MonthDay");

            if (year.Length != 4 || monthDay.Length < 4)
            {
                return RaceScheduleCreateResult.InvalidDate;
            }

            string month = monthDay.Substring(0, 2);
            string day = monthDay.Substring(2, 2);
            string raceDate = year + month + day;

            // 出力対象は実行日だけに統一
            if (!string.Equals(raceDate, executionDateYmd, StringComparison.Ordinal))
            {
                return RaceScheduleCreateResult.SkipByDate;
            }

            string jyoCd = Pad2(Pick(d, "JyoCD"));
            string kaiji = Pad2(Pick(d, "Kaiji"));
            string nichiji = Pad2(Pick(d, "Nichiji"));
            string raceNum = Pad2(Pick(d, "RaceNum"));

            if (jyoCd.Length != 2 || kaiji.Length != 2 || nichiji.Length != 2 || raceNum.Length != 2)
            {
                return RaceScheduleCreateResult.InvalidRaceKey;
            }

            if (!IsJraPlace(jyoCd))
            {
                return RaceScheduleCreateResult.InvalidPlace;
            }

            string rawHassoTime = Pick(d, "HassoTime", "HassoJikoku", "StartTime");

            DateTime? startDateTime = BuildDateTimeFromYmdAndHm(raceDate, rawHassoTime);

            if (!startDateTime.HasValue)
            {
                return RaceScheduleCreateResult.InvalidStartTime;
            }

            var r = new RaceScheduleRecord();

            r.RaceDate = raceDate;
            r.Year = year;
            r.Month = ToIntText(month);
            r.Day = ToIntText(day);

            r.JyoCd = jyoCd;
            r.Kaiji = kaiji;
            r.Nichiji = nichiji;
            r.RaceNum = raceNum;

            r.RaceKey = raceDate + jyoCd + kaiji + nichiji + raceNum;

            // ユーザーDB用 race_id
            // 年 + 競馬場コード + 回 + 日目 + レース番号
            r.RaceId = year + jyoCd + kaiji + nichiji + raceNum;

            string raceName = Pick(d, "Hondai");
            if (raceName.Length == 0)
            {
                raceName = PickByKeyContains(d, "Hondai", "RaceName", "Ryakusyo10");
            }

            r.RaceName = raceName;

            r.RawHassoTime = rawHassoTime;

            // 発走時刻
            r.StartRaceTimeText = startDateTime.Value.ToString("HH:mm:ss");

            // 確認用に日時つきも残す
            r.StartRaceDateTimeText = startDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

            // ここを時刻だけに変更
            r.M30TargetTimeText = startDateTime.Value.AddMinutes(-30).ToString("HH:mm:ss");
            r.M10TargetTimeText = startDateTime.Value.AddMinutes(-10).ToString("HH:mm:ss");
            r.M5TargetTimeText = startDateTime.Value.AddMinutes(-5).ToString("HH:mm:ss");

            r.Entry = ToIntText(Pick(d, "SyussoTosu", "ShussoTosu", "Tosu", "HeadCount"));
            r.CourseDistance = ToIntText(Pick(d, "Kyori", "Distance"));
            r.Track = ToIntText(Pick(d, "TrackCD"));

            record = r;
            return RaceScheduleCreateResult.Success;
        }

        private static string Pick(Dictionary<string, string> d, params string[] candidatesOrSuffixes)
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

        private static string PickByKeyContains(Dictionary<string, string> d, params string[] tokens)
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

        private static string Norm(string s)
        {
            if (s == null)
            {
                return "";
            }

            return s.Trim(' ', '　');
        }

        private static string Pad2(string s)
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

        private static string ToIntText(string raw)
        {
            raw = Norm(raw);

            if (raw.Length == 0)
            {
                return "";
            }

            string digits = ExtractSignedDigits(raw);

            if (digits.Length == 0)
            {
                return "";
            }

            int n;
            if (!int.TryParse(digits, out n))
            {
                return "";
            }

            return n.ToString();
        }

        private static string ExtractSignedDigits(string s)
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

        private static string ExtractDigits(string s)
        {
            if (s == null) return "";

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

        private static DateTime? BuildDateTimeFromYmdAndHm(string ymd, string hm)
        {
            ymd = ExtractDigits(ymd);
            hm = ExtractDigits(hm);

            if (ymd.Length != 8)
            {
                return null;
            }

            if (hm.Length == 3)
            {
                hm = "0" + hm;
            }

            if (hm.Length != 4)
            {
                return null;
            }

            int year;
            int month;
            int day;
            int hour;
            int minute;

            if (!int.TryParse(ymd.Substring(0, 4), out year)) return null;
            if (!int.TryParse(ymd.Substring(4, 2), out month)) return null;
            if (!int.TryParse(ymd.Substring(6, 2), out day)) return null;
            if (!int.TryParse(hm.Substring(0, 2), out hour)) return null;
            if (!int.TryParse(hm.Substring(2, 2), out minute)) return null;

            try
            {
                return new DateTime(year, month, day, hour, minute, 0);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsJraPlace(string placeCode)
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
    }

    internal enum FetchResult
    {
        Success,
        DoneButEmpty,
        Failed
    }

    internal sealed class JvLinkClient : IDisposable
    {
        private const string ProgId = "JVDTLab.JVLink";

        private readonly Logger _logger;
        private dynamic _jv;
        private bool _created;

        public JvLinkClient(Logger logger)
        {
            _logger = logger;

            Type t = Type.GetTypeFromProgID(ProgId);

            if (t == null)
            {
                throw new Exception("JV-Link COMが見つかりません。ProgID=" + ProgId + "。JRA-VAN DataLab SDKのインストールとx86ビルドを確認してください。");
            }

            _jv = Activator.CreateInstance(t);
            _created = true;

            _logger.Info("JV-Link COM created. ProgID=" + ProgId);
        }

        public int Init(string sid)
        {
            return _jv.JVInit(sid);
        }

        public int Open(string dataSpec, string fromTime, int option, ref int readCount, ref int downloadCount, ref string lastTs)
        {
            return _jv.JVOpen(dataSpec, fromTime, option, ref readCount, ref downloadCount, ref lastTs);
        }

        public int Gets(ref object objBuff, int bufferSize, out string fileName)
        {
            fileName = "";

            try
            {
                return _jv.JVGets(ref objBuff, bufferSize, out fileName);
            }
            catch (Exception ex) when (IsRuntimeBinderException(ex))
            {
                string fname = "";
                int rc = _jv.JVGets(ref objBuff, bufferSize, ref fname);
                fileName = fname;
                return rc;
            }
        }

        public int Close()
        {
            if (!_created || _jv == null)
            {
                return 0;
            }

            return _jv.JVClose();
        }

        public void Dispose()
        {
            try
            {
                Close();
            }
            catch
            {
            }

            _jv = null;
            _created = false;
        }

        private static bool IsRuntimeBinderException(Exception ex)
        {
            return ex.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException";
        }
    }

    internal sealed class Logger : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        public Logger(string logPath)
        {
            string dir = Path.GetDirectoryName(logPath);

            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _writer = new StreamWriter(logPath, true, _utf8NoBom);
            _writer.AutoFlush = true;
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                          " [" + level + "] " +
                          message;

            lock (_lock)
            {
                Console.WriteLine(line);
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer.Flush();
                _writer.Dispose();
            }
        }
    }

    internal sealed class ManifestWriter : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        public ManifestWriter(string manifestPath)
        {
            string dir = Path.GetDirectoryName(manifestPath);

            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _writer = new StreamWriter(manifestPath, false, _utf8NoBom);
            _writer.AutoFlush = true;
        }

        public void WriteLine(string line)
        {
            lock (_lock)
            {
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer.Flush();
                _writer.Dispose();
            }
        }
    }
}