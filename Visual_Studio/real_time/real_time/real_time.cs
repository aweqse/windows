using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace real_time
{
    // ============================================================
    // ここだけ変更すればOK：本文固定設定
    // ============================================================
    internal static class AppConfig
    {
        // 出力先フォルダ
        // ログ・manifest・rawファイルはすべてこの配下に出力する。
        public const string OutputDir = @"C:\Users\dev-w\Desktop\workspace\output\log";

        // 0B31用 race_key ファイル。
        // OutputDir 配下の race_keys.txt を読む。
        // 例：
        // C:\Users\dev-w\Desktop\workspace\output\log\race_keys.txt
        public static readonly string RaceKeysFile = Path.Combine(OutputDir, "race_keys.txt");

        // 対象日。基本はPCの今日の日付。
        public static readonly string DateYmd = DateTime.Now.ToString("yyyyMMdd");

        // 取得タイミング。30 / 10 / 5 のどれか。
        public const int SnapshotMin = 5;

        // 取得対象
        // 0B11 = 速報馬体重
        // 0B14 = 速報開催情報
        // 0B31 = 単勝・複勝・枠連オッズ
        public const string SpecsText = "0B11,0B14,0B31";

        // JVInit に渡すSID
        public const string Sid = "UNKNOWN";

        // 0B11/0B14 で YYYYMMDD と YYYYMMDD000000 の両方を試すか。
        public const bool TryKeyVariants = true;

        // JVRead設定
        public const int BufferSize = 110000;
        public const int MaxEmptyCount = 20;
        public const int ReadWaitMs = 3000;

        // 0B31用 race_key をコード本文に直接書きたい場合はここに書く。
        //
        // 例：
        // public const string RaceKeysText = @"
        // 2026052304010701
        // 2026052304010702
        // 2026052304010703
        // ";
        //
        // 空なら RaceKeysFile を読む。
        public const string RaceKeysText = "";
    }

    internal class Program
    {
        /*
         * real_time
         *
         * JV-Link リアルタイム系データ取得用。
         *
         * 引数なしで実行できる版。
         *
         * 本文固定設定：
         *   対象日        = PCの現在日付 yyyyMMdd
         *   snapshot_min  = 5
         *   出力先        = AppConfig.OutputDir
         *   race_keys     = AppConfig.RaceKeysFile
         *   取得対象      = 0B11,0B14,0B31
         *
         * 取得対象：
         *   0B11 = 速報馬体重
         *   0B14 = 速報開催情報
         *   0B31 = 単勝・複勝・枠連オッズ
         *
         * 重要：
         *   0B31 は race_key が必要。
         *
         * race_key の指定方法：
         *   1. AppConfig.RaceKeysFile に1行1件で書く
         *   2. または AppConfig.RaceKeysText に直接書く
         *   3. または引数 --race-key / --race-keys-file で渡す
         *
         * 出力ファイル例：
         *   AppConfig.OutputDir\20260523.log
         *   AppConfig.OutputDir\20260523_manifest.txt
         *   AppConfig.OutputDir\20260523_0B11_20260523.raw.txt
         *   AppConfig.OutputDir\20260523_0B14_20260523.raw.txt
         *   AppConfig.OutputDir\20260523_0B31_2026052304010701.raw.txt
         *
         * 注意：
         *   Visual Studioでは必ず x86 でビルドすること。
         *   JV-Link COMは32bit前提。
         */

        private const string MutexName = @"Global\KeibaRealtimeRawGetterTestMutex";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("============================================================");
            Console.WriteLine("real_time START");
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
                        Console.WriteLine("[SKIP] 既に同じ取得プログラムが実行中です。JV-Link同時実行防止のため終了します。");
                        return 10;
                    }

                    var runner = new RealtimeGetterRunner(options);
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
            Console.WriteLine("  引数なしで実行可能です。本文固定設定を使用します。");
            Console.WriteLine();
            Console.WriteLine("Optional:");
            Console.WriteLine("  real_time.exe --date YYYYMMDD --snapshot-min 30|10|5 --output-dir PATH");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --date YYYYMMDD                  任意。対象日。省略時はAppConfig.DateYmd。例：20260523");
            Console.WriteLine("  --snapshot-min 30|10|5           任意。省略時はAppConfig.SnapshotMin。");
            Console.WriteLine("  --output-dir PATH                任意。省略時はAppConfig.OutputDir。");
            Console.WriteLine("  --specs 0B11,0B14,0B31           任意。省略時はAppConfig.SpecsText。");
            Console.WriteLine("  --sid SID                        任意。JVInitに渡すSID。省略時はAppConfig.Sid。");
            Console.WriteLine("  --rt-key KEY                     任意。全DataSpec共通でJVRTOpenに渡すキー。");
            Console.WriteLine("  --race-key KEY                   任意。0B31用race_key。");
            Console.WriteLine("  --race-keys-file PATH            任意。0B31用race_key一覧。1行1件。省略時はAppConfig.RaceKeysFile。");
            Console.WriteLine("  --try-key-variants 0|1           任意。0B11/0B14で複数キー形式を試す。省略時はAppConfig.TryKeyVariants。");
            Console.WriteLine("  --buffer-size N                  任意。JVReadバッファサイズ。省略時はAppConfig.BufferSize。");
            Console.WriteLine("  --max-empty-count N              任意。空読み最大回数。省略時はAppConfig.MaxEmptyCount。");
            Console.WriteLine("  --read-wait-ms N                 任意。待機系rc時の待機ms。省略時はAppConfig.ReadWaitMs。");
            Console.WriteLine();
        }
    }

    internal sealed class RunOptions
    {
        public string DateYmd { get; private set; }
        public int SnapshotMin { get; private set; }
        public string OutputDir { get; private set; }
        public List<string> Specs { get; private set; }
        public string Sid { get; private set; }
        public string RtKey { get; private set; }
        public string RaceKey { get; private set; }
        public string RaceKeysFile { get; private set; }
        public List<string> RaceKeys { get; private set; }
        public bool TryKeyVariants { get; private set; }
        public int BufferSize { get; private set; }
        public int MaxEmptyCount { get; private set; }
        public int ReadWaitMs { get; private set; }

        private RunOptions()
        {
            Specs = new List<string>();
            RaceKeys = new List<string>();

            DateYmd = AppConfig.DateYmd;
            SnapshotMin = AppConfig.SnapshotMin;
            OutputDir = AppConfig.OutputDir;

            Sid = AppConfig.Sid;
            RtKey = "";
            RaceKey = "";
            RaceKeysFile = AppConfig.RaceKeysFile;

            TryKeyVariants = AppConfig.TryKeyVariants;

            BufferSize = AppConfig.BufferSize;
            MaxEmptyCount = AppConfig.MaxEmptyCount;
            ReadWaitMs = AppConfig.ReadWaitMs;
        }

        public static RunOptions Parse(string[] args)
        {
            var opt = new RunOptions();
            var dict = ParseArgs(args);

            if (dict.TryGetValue("--date", out string dateText) && !string.IsNullOrWhiteSpace(dateText))
            {
                opt.DateYmd = dateText.Trim();
            }

            if (!IsYmd(opt.DateYmd))
            {
                throw new Exception("--date は YYYYMMDD 形式で指定してください。例：20260523");
            }

            if (dict.TryGetValue("--snapshot-min", out string snapshotText) && !string.IsNullOrWhiteSpace(snapshotText))
            {
                if (!int.TryParse(snapshotText, out int snapshotMin))
                {
                    throw new Exception("--snapshot-min は数値で指定してください。例：5");
                }

                opt.SnapshotMin = snapshotMin;
            }

            if (opt.SnapshotMin != 30 && opt.SnapshotMin != 10 && opt.SnapshotMin != 5)
            {
                throw new Exception("--snapshot-min は 30, 10, 5 のいずれかで指定してください。");
            }

            if (dict.TryGetValue("--output-dir", out string outputDir) && !string.IsNullOrWhiteSpace(outputDir))
            {
                opt.OutputDir = outputDir.Trim();

                // --output-dir を指定した場合、--race-keys-file 未指定なら
                // race_keys.txt もその出力先配下を見るようにする。
                if (!dict.ContainsKey("--race-keys-file"))
                {
                    opt.RaceKeysFile = Path.Combine(opt.OutputDir, "race_keys.txt");
                }
            }

            string specsText = AppConfig.SpecsText;

            if (dict.TryGetValue("--specs", out string argSpecsText) && !string.IsNullOrWhiteSpace(argSpecsText))
            {
                specsText = argSpecsText;
            }

            opt.Specs = specsText
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (opt.Specs.Count == 0)
            {
                throw new Exception("取得対象 specs が空です。");
            }

            if (dict.TryGetValue("--sid", out string sid))
            {
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    opt.Sid = sid.Trim();
                }
            }

            if (dict.TryGetValue("--rt-key", out string rtKey))
            {
                opt.RtKey = rtKey.Trim();
            }

            if (dict.TryGetValue("--race-key", out string raceKey))
            {
                opt.RaceKey = raceKey.Trim();

                if (!string.IsNullOrWhiteSpace(opt.RaceKey))
                {
                    opt.RaceKeys.Add(opt.RaceKey);
                }
            }

            if (dict.TryGetValue("--race-keys-file", out string raceKeysFile))
            {
                opt.RaceKeysFile = raceKeysFile.Trim();
            }

            LoadDefaultRaceKeys(opt);

            opt.RaceKeys = opt.RaceKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !x.StartsWith("#"))
                .Distinct()
                .ToList();

            if (dict.TryGetValue("--try-key-variants", out string tryKeyText))
            {
                opt.TryKeyVariants = ParseBoolLike(tryKeyText);
            }

            if (dict.TryGetValue("--buffer-size", out string bufferText))
            {
                if (!int.TryParse(bufferText, out int bufferSize))
                {
                    throw new Exception("--buffer-size は数値で指定してください。");
                }

                opt.BufferSize = Math.Max(1024, bufferSize);
            }

            if (dict.TryGetValue("--max-empty-count", out string maxEmptyText))
            {
                if (!int.TryParse(maxEmptyText, out int maxEmptyCount))
                {
                    throw new Exception("--max-empty-count は数値で指定してください。");
                }

                opt.MaxEmptyCount = Math.Max(1, maxEmptyCount);
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

        private static void LoadDefaultRaceKeys(RunOptions opt)
        {
            // コード本文に直接書かれている race_key を読む
            if (!string.IsNullOrWhiteSpace(AppConfig.RaceKeysText))
            {
                var keysFromText = AppConfig.RaceKeysText
                    .Replace("\r", "\n")
                    .Split(new[] { '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .Where(x => !x.StartsWith("#"))
                    .ToList();

                opt.RaceKeys.AddRange(keysFromText);
            }

            // ファイルから race_key を読む
            if (!string.IsNullOrWhiteSpace(opt.RaceKeysFile) && File.Exists(opt.RaceKeysFile))
            {
                var keysFromFile = File.ReadAllLines(opt.RaceKeysFile, Encoding.UTF8)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .Where(x => !x.StartsWith("#"))
                    .ToList();

                opt.RaceKeys.AddRange(keysFromFile);
            }
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

        private static bool IsYmd(string ymd)
        {
            if (string.IsNullOrWhiteSpace(ymd)) return false;
            if (ymd.Length != 8) return false;
            return ymd.All(char.IsDigit);
        }

        private static bool ParseBoolLike(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string v = value.Trim().ToLowerInvariant();

            return v == "1" ||
                   v == "true" ||
                   v == "yes" ||
                   v == "y" ||
                   v == "on";
        }
    }

    internal sealed class RealtimeGetterRunner
    {
        private readonly RunOptions _options;
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        private string _runDir;
        private string _fileDateBase;
        private Logger _logger;
        private ManifestWriter _manifest;

        public RealtimeGetterRunner(RunOptions options)
        {
            _options = options;
        }

        public int Run()
        {
            DateTime startedAt = DateTime.Now;

            try
            {
                PrepareOutputDirectory(startedAt);

                string logPath = Path.Combine(_runDir, _fileDateBase + ".log");
                string manifestPath = Path.Combine(_runDir, _fileDateBase + "_manifest.txt");

                _logger = new Logger(logPath);
                _manifest = new ManifestWriter(manifestPath);

                WriteStartLog(startedAt, logPath, manifestPath);

                bool anyFailed = false;
                bool anySkipped = false;

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

                    foreach (string spec in _options.Specs)
                    {
                        FetchResult result = FetchOneSpec(jv, spec);

                        if (result == FetchResult.Failed)
                        {
                            anyFailed = true;
                        }

                        if (result == FetchResult.Skipped)
                        {
                            anySkipped = true;
                        }
                    }

                    try
                    {
                        jv.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("JVCloseで例外: " + ex.Message);
                    }
                }

                DateTime finishedAt = DateTime.Now;

                _logger.Info("");
                _logger.Info("finished_at       = " + finishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                _logger.Info("elapsed_seconds   = " + (finishedAt - startedAt).TotalSeconds.ToString("0.000"));

                _manifest.WriteLine("");
                _manifest.WriteLine("finished_at=" + finishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                _manifest.WriteLine("elapsed_seconds=" + (finishedAt - startedAt).TotalSeconds.ToString("0.000"));

                if (anyFailed)
                {
                    _logger.Warn("overall_status    = done_with_failed_spec");
                    _manifest.WriteLine("status=done_with_failed_spec");

                    _logger.Info("============================================================");
                    _logger.Info("real_time END");
                    _logger.Info("============================================================");

                    return 30;
                }

                if (anySkipped)
                {
                    _logger.Warn("overall_status    = done_with_skipped_spec");
                    _manifest.WriteLine("status=done_with_skipped_spec");

                    _logger.Info("============================================================");
                    _logger.Info("real_time END");
                    _logger.Info("============================================================");

                    return 31;
                }

                _logger.Info("overall_status    = done");
                _manifest.WriteLine("status=done");

                _logger.Info("============================================================");
                _logger.Info("real_time END");
                _logger.Info("============================================================");

                return 0;
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

        private void PrepareOutputDirectory(DateTime startedAt)
        {
            _fileDateBase = startedAt.ToString("yyyyMMdd");

            string outputRoot = Path.GetFullPath(_options.OutputDir);

            _runDir = outputRoot;

            Directory.CreateDirectory(_runDir);
        }

        private void WriteStartLog(DateTime startedAt, string logPath, string manifestPath)
        {
            _logger.Info("============================================================");
            _logger.Info("real_time START");
            _logger.Info("============================================================");
            _logger.Info("started_at        = " + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _logger.Info("execution_date    = " + startedAt.ToString("yyyyMMdd"));
            _logger.Info("file_date_base    = " + _fileDateBase);
            _logger.Info("target_date       = " + _options.DateYmd);
            _logger.Info("snapshot_min      = " + _options.SnapshotMin);
            _logger.Info("output_dir        = " + Path.GetFullPath(_options.OutputDir));
            _logger.Info("run_dir           = " + _runDir);
            _logger.Info("log_path          = " + logPath);
            _logger.Info("manifest_path     = " + manifestPath);
            _logger.Info("specs             = " + string.Join(",", _options.Specs));
            _logger.Info("sid               = " + MaskSid(_options.Sid));
            _logger.Info("sid_empty         = " + string.IsNullOrEmpty(_options.Sid));
            _logger.Info("rt_key            = " + _options.RtKey);
            _logger.Info("race_key          = " + _options.RaceKey);
            _logger.Info("race_keys_file    = " + _options.RaceKeysFile);
            _logger.Info("race_keys_count   = " + _options.RaceKeys.Count);
            _logger.Info("race_keys         = " + string.Join(",", _options.RaceKeys));
            _logger.Info("try_key_variants  = " + _options.TryKeyVariants);
            _logger.Info("buffer_size       = " + _options.BufferSize);
            _logger.Info("max_empty_count   = " + _options.MaxEmptyCount);
            _logger.Info("read_wait_ms      = " + _options.ReadWaitMs);
            _logger.Info("");

            _manifest.WriteLine("started_at=" + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _manifest.WriteLine("execution_date=" + startedAt.ToString("yyyyMMdd"));
            _manifest.WriteLine("file_date_base=" + _fileDateBase);
            _manifest.WriteLine("target_date=" + _options.DateYmd);
            _manifest.WriteLine("snapshot_min=" + _options.SnapshotMin);
            _manifest.WriteLine("output_dir=" + Path.GetFullPath(_options.OutputDir));
            _manifest.WriteLine("run_dir=" + _runDir);
            _manifest.WriteLine("log_path=" + logPath);
            _manifest.WriteLine("manifest_path=" + manifestPath);
            _manifest.WriteLine("specs=" + string.Join(",", _options.Specs));
            _manifest.WriteLine("sid=" + MaskSid(_options.Sid));
            _manifest.WriteLine("sid_empty=" + string.IsNullOrEmpty(_options.Sid));
            _manifest.WriteLine("rt_key=" + _options.RtKey);
            _manifest.WriteLine("race_key=" + _options.RaceKey);
            _manifest.WriteLine("race_keys_file=" + _options.RaceKeysFile);
            _manifest.WriteLine("race_keys_count=" + _options.RaceKeys.Count);
            _manifest.WriteLine("race_keys=" + string.Join(",", _options.RaceKeys));
            _manifest.WriteLine("try_key_variants=" + _options.TryKeyVariants);
            _manifest.WriteLine("buffer_size=" + _options.BufferSize);
            _manifest.WriteLine("max_empty_count=" + _options.MaxEmptyCount);
            _manifest.WriteLine("read_wait_ms=" + _options.ReadWaitMs);
            _manifest.WriteLine("");
        }

        private FetchResult FetchOneSpec(JvLinkClient jv, string dataSpec)
        {
            List<string> keys = BuildKeysForSpec(dataSpec, out bool skipped, out string skipReason);

            if (skipped)
            {
                _logger.Warn("DataSpec SKIP: " + dataSpec + " reason=" + skipReason);

                _manifest.WriteLine("[" + dataSpec + ":SKIPPED]");
                _manifest.WriteLine("reason=" + skipReason);
                _manifest.WriteLine("status=skipped");
                _manifest.WriteLine("");

                return FetchResult.Skipped;
            }

            bool anySuccess = false;
            bool anyFailed = false;

            foreach (string rtKey in keys)
            {
                bool ok = FetchOneSpecOneKey(jv, dataSpec, rtKey);

                if (ok)
                {
                    anySuccess = true;

                    if (_options.TryKeyVariants && !dataSpec.Equals("0B31", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
                else
                {
                    anyFailed = true;
                }
            }

            if (anySuccess)
            {
                return FetchResult.Success;
            }

            return anyFailed ? FetchResult.Failed : FetchResult.Skipped;
        }

        private bool FetchOneSpecOneKey(JvLinkClient jv, string dataSpec, string rtKey)
        {
            DateTime specStartedAt = DateTime.Now;

            _logger.Info("");
            _logger.Info("------------------------------------------------------------");
            _logger.Info("DataSpec START: " + dataSpec + " key=" + rtKey);
            _logger.Info("------------------------------------------------------------");

            string rawFileName = BuildRawFileName(_fileDateBase, dataSpec, rtKey);
            string rawPath = Path.Combine(_runDir, rawFileName);

            int openRc;

            try
            {
                openRc = jv.RtOpen(dataSpec, rtKey);
            }
            catch (Exception ex)
            {
                _logger.Error("JVRTOpen exception. dataSpec=" + dataSpec + ", key=" + rtKey + ", error=" + ex);

                _manifest.WriteLine("[" + dataSpec + ":" + rtKey + "]");
                _manifest.WriteLine("raw_file=" + rawFileName);
                _manifest.WriteLine("rt_key=" + rtKey);
                _manifest.WriteLine("JVRTOpen_exception=" + ex.Message);
                _manifest.WriteLine("status=failed_rtopen_exception");
                _manifest.WriteLine("");

                return false;
            }

            _logger.Info("JVRTOpen dataSpec    = " + dataSpec);
            _logger.Info("JVRTOpen key         = " + rtKey);
            _logger.Info("JVRTOpen rc          = " + openRc);

            _manifest.WriteLine("[" + dataSpec + ":" + rtKey + "]");
            _manifest.WriteLine("raw_file=" + rawFileName);
            _manifest.WriteLine("rt_key=" + rtKey);
            _manifest.WriteLine("JVRTOpen_rc=" + openRc);

            if (openRc < 0)
            {
                _logger.Error("JVRTOpen failed. dataSpec=" + dataSpec + ", key=" + rtKey + ", rc=" + openRc);
                _logger.Error("JVRTOpen failed detail: dataSpec=" + dataSpec + ", rtKey=" + rtKey + ", target_date=" + _options.DateYmd + ", snapshot_min=" + _options.SnapshotMin);

                _manifest.WriteLine("target_date=" + _options.DateYmd);
                _manifest.WriteLine("snapshot_min=" + _options.SnapshotMin);
                _manifest.WriteLine("status=failed_rtopen");
                _manifest.WriteLine("");

                return false;
            }

            int totalRecordCount = 0;
            int totalBytesApprox = 0;
            int fileSwitchCount = 0;
            int retryWaitCount = 0;
            int emptyCount = 0;
            string lastJvFileName = "";

            using (var writer = new StreamWriter(rawPath, false, _utf8NoBom))
            {
                while (true)
                {
                    string buffer;
                    string jvFileName;

                    int readRc;

                    try
                    {
                        readRc = jv.Read(out buffer, _options.BufferSize, out jvFileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("JVRead exception. dataSpec=" + dataSpec + ", key=" + rtKey + ", error=" + ex);
                        _manifest.WriteLine("JVRead_exception=" + ex.Message);
                        break;
                    }

                    if (!string.IsNullOrEmpty(jvFileName))
                    {
                        lastJvFileName = jvFileName;
                    }

                    if (readRc > 0)
                    {
                        emptyCount = 0;
                        totalRecordCount++;
                        totalBytesApprox += readRc;

                        writer.WriteLine(buffer);

                        if (totalRecordCount <= 5)
                        {
                            _logger.Info("JVRead record sample #" + totalRecordCount +
                                         " rc=" + readRc +
                                         " file=" + jvFileName +
                                         " head=" + SafeHead(buffer, 100));
                        }

                        if (totalRecordCount % 1000 == 0)
                        {
                            _logger.Info("JVRead progress dataSpec=" + dataSpec +
                                         " key=" + rtKey +
                                         " records=" + totalRecordCount +
                                         " last_rc=" + readRc +
                                         " file=" + jvFileName);
                        }

                        continue;
                    }

                    if (readRc == 0)
                    {
                        _logger.Info("JVRead rc=0. dataSpec=" + dataSpec + " key=" + rtKey + " 読み込み終了。");
                        break;
                    }

                    if (readRc == -1)
                    {
                        fileSwitchCount++;
                        _logger.Info("JVRead rc=-1. dataSpec=" + dataSpec + " key=" + rtKey + " ファイル切替。count=" + fileSwitchCount);
                        continue;
                    }

                    if (readRc == -3)
                    {
                        retryWaitCount++;
                        _logger.Warn("JVRead rc=-3. dataSpec=" + dataSpec + " key=" + rtKey + " 待機。count=" + retryWaitCount);
                        Thread.Sleep(_options.ReadWaitMs);
                        continue;
                    }

                    if (readRc < 0)
                    {
                        _logger.Error("JVRead error. dataSpec=" + dataSpec + ", key=" + rtKey + ", rc=" + readRc);
                        break;
                    }

                    emptyCount++;

                    if (emptyCount >= _options.MaxEmptyCount)
                    {
                        _logger.Warn("JVRead empty limit reached. dataSpec=" + dataSpec + ", key=" + rtKey + ", emptyCount=" + emptyCount);
                        break;
                    }
                }
            }

            try
            {
                jv.Close();
            }
            catch (Exception ex)
            {
                _logger.Warn("JVClose exception after dataSpec=" + dataSpec + ", key=" + rtKey + ", error=" + ex.Message);
            }

            var fileInfo = new FileInfo(rawPath);
            DateTime specFinishedAt = DateTime.Now;

            _logger.Info("DataSpec finished: " + dataSpec + " key=" + rtKey);
            _logger.Info("raw_path             = " + rawPath);
            _logger.Info("raw_size_bytes       = " + fileInfo.Length);
            _logger.Info("record_count         = " + totalRecordCount);
            _logger.Info("bytes_approx         = " + totalBytesApprox);
            _logger.Info("file_switch_count    = " + fileSwitchCount);
            _logger.Info("retry_wait_count     = " + retryWaitCount);
            _logger.Info("last_jv_file_name    = " + lastJvFileName);
            _logger.Info("elapsed_seconds      = " + (specFinishedAt - specStartedAt).TotalSeconds.ToString("0.000"));

            _manifest.WriteLine("raw_path=" + rawPath);
            _manifest.WriteLine("raw_size_bytes=" + fileInfo.Length);
            _manifest.WriteLine("record_count=" + totalRecordCount);
            _manifest.WriteLine("bytes_approx=" + totalBytesApprox);
            _manifest.WriteLine("file_switch_count=" + fileSwitchCount);
            _manifest.WriteLine("retry_wait_count=" + retryWaitCount);
            _manifest.WriteLine("last_jv_file_name=" + lastJvFileName);
            _manifest.WriteLine("started_at=" + specStartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _manifest.WriteLine("finished_at=" + specFinishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            _manifest.WriteLine("elapsed_seconds=" + (specFinishedAt - specStartedAt).TotalSeconds.ToString("0.000"));

            if (totalRecordCount > 0)
            {
                _manifest.WriteLine("status=done");
            }
            else
            {
                _manifest.WriteLine("status=done_but_empty");
            }

            _manifest.WriteLine("");

            _logger.Info("------------------------------------------------------------");
            _logger.Info("DataSpec END: " + dataSpec + " key=" + rtKey);
            _logger.Info("------------------------------------------------------------");

            return true;
        }

        private List<string> BuildKeysForSpec(string dataSpec, out bool skipped, out string skipReason)
        {
            skipped = false;
            skipReason = "";

            if (!string.IsNullOrWhiteSpace(_options.RtKey))
            {
                return new List<string> { _options.RtKey.Trim() };
            }

            if (dataSpec.Equals("0B31", StringComparison.OrdinalIgnoreCase))
            {
                if (_options.RaceKeys.Count > 0)
                {
                    return _options.RaceKeys;
                }

                skipped = true;
                skipReason = "0B31 requires race_key. Create race_keys.txt or set AppConfig.RaceKeysText.";
                return new List<string>();
            }

            var keys = new List<string>
            {
                _options.DateYmd
            };

            if (_options.TryKeyVariants)
            {
                keys.Add(_options.DateYmd + "000000");
            }

            return keys.Distinct().ToList();
        }

        private static string BuildRawFileName(string fileDateBase, string dataSpec, string rtKey)
        {
            string safeKey = MakeSafeFileName(rtKey);

            if (string.IsNullOrWhiteSpace(safeKey))
            {
                return fileDateBase + "_" + dataSpec + ".raw.txt";
            }

            return fileDateBase + "_" + dataSpec + "_" + safeKey + ".raw.txt";
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();

            foreach (char c in value)
            {
                if (invalid.Contains(c))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
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

    internal enum FetchResult
    {
        Success,
        Failed,
        Skipped
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

        public int RtOpen(string dataSpec, string rtKey)
        {
            return _jv.JVRTOpen(dataSpec, rtKey);
        }

        public int Read(out string buffer, int bufferSize, out string fileName)
        {
            buffer = "";
            fileName = "";

            string buff = "";
            string fname = "";

            try
            {
                int rc = _jv.JVRead(ref buff, bufferSize, ref fname);

                buffer = buff;
                fileName = fname;

                return rc;
            }
            catch (Exception ex) when (IsRuntimeBinderException(ex))
            {
                buff = "";
                fname = "";

                try
                {
                    int rc = _jv.JVRead(ref buff, ref fname);

                    buffer = buff;
                    fileName = fname;

                    return rc;
                }
                catch (Exception ex2) when (IsRuntimeBinderException(ex2))
                {
                    throw new Exception("JVReadの呼び出し形式が合いません。JV-LinkのJVReadシグネチャを確認してください。", ex2);
                }
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