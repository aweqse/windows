using System;
using System.IO;
using System.Text;
using System.Threading;
using JVDTLabLib;

namespace jockey_info
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            JVLinkClass jv = new JVLinkClass();

            int readCount = 0;
            int downloadCount = 0;
            string lastFileTimestamp;

            try
            {
                int rc = jv.JVInit("UNKNOWN");
                Console.WriteLine($"JVInit rc={rc}");
                if (rc != 0) return;

                // まずは RACE の通常データで JVRead 自体が通るか確認
                string dataSpec = "RACE";
                string fromTime = DateTime.Now.AddDays(-14).ToString("yyyyMMdd") + "000000";
                int option = 1;

                rc = jv.JVOpen(
                    dataSpec,
                    fromTime,
                    option,
                    ref readCount,
                    ref downloadCount,
                    out lastFileTimestamp
                );

                Console.WriteLine($"JVOpen({dataSpec}) rc={rc}, read={readCount}, dl={downloadCount}, lastTs={lastFileTimestamp}");
                if (rc != 0)
                {
                    try { jv.JVClose(); } catch { }
                    return;
                }

                int loop = 0;

                while (true)
                {
                    string buff;
                    int size;
                    string fileName;

                    rc = jv.JVRead(out buff, out size, out fileName);

                    if (rc == 0)
                    {
                        loop++;

                        string recId = "";
                        if (!string.IsNullOrEmpty(buff) && buff.Length >= 2)
                            recId = buff.Substring(0, 2);

                        Console.WriteLine($"[{loop}] rc=0 recId={recId} size={size} file={fileName}");

                        if (loop >= 20)
                            break;
                    }
                    else if (rc == -1)
                    {
                        Console.WriteLine("JVRead end");
                        break;
                    }
                    else if (rc == -3)
                    {
                        using System;
                        using System.Collections.Generic;
                        using System.IO;
                        using System.Text;

class DumpProgram
        {
            [STAThread]
            static void Main()
            {
                string fromTime = "20200101000000";
                int option = 4;

                string outDir = @"C:\Users\dev-w\Desktop\workspace\Visual_Studio\output";
                Directory.CreateDirectory(outDir);

                string outCsv = Path.Combine(outDir, "jockey_belong_info.csv");

                Console.WriteLine("LOG_VERSION=jockey_belong_same_style_v1");
                Console.WriteLine("CurrentDirectory=" + Environment.CurrentDirectory);
                Console.WriteLine("CSV path=" + Path.GetFullPath(outCsv));

                var t = Type.GetTypeFromProgID("JVDTLab.JVLink");
                if (t == null)
                {
                    Console.WriteLine("[NG] JV-Link(COM) が見つかりません。");
                    Console.ReadLine();
                    return;
                }

                dynamic jv = Activator.CreateInstance(t);

                int rc = jv.JVInit("JockeyBelongRaw");
                if (rc != 0)
                {
                    Console.WriteLine("[NG] JVInit err=" + rc);
                    Console.ReadLine();
                    return;
                }

                ExportJockeyBelong(jv, fromTime, option, outCsv);

                Console.WriteLine("Press Enter...");
                Console.ReadLine();
            }

            static void ExportJockeyBelong(dynamic jv, string fromTime, int option, string outCsv)
            {
                int readCount = 0;
                int downloadCount = 0;
                string lastTs = "";

                int rc = jv.JVOpen("DIFF", fromTime, option, ref readCount, ref downloadCount, ref lastTs);
                Console.WriteLine("JVOpen(DIFF) rc=" + rc + ", read=" + readCount + ", dl=" + downloadCount + ", lastTs=" + lastTs);

                if (rc != 0)
                {
                    Console.WriteLine("[NG] JVOpen失敗 rc=" + rc);
                    try { jv.JVClose(); } catch { }
                    return;
                }

                object objBuff = Array.Empty<byte>();
                const int BUFSIZE = 110000;
                var sjis = Encoding.GetEncoding("shift_jis");

                int totalCount = 0;
                int ksCount = 0;

                using (var sw = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
                {
                    sw.WriteLine(string.Join(",",
                        Csv("jockey_code"),
                        Csv("jockey_name"),
                        Csv("jockey_name_short"),
                        Csv("belong_code"),
                        Csv("belong_name"),
                        Csv("invite_region_name"),
                        Csv("belong_trainer_code"),
                        Csv("belong_trainer_name_short"),
                        Csv("deleted_flag"),
                        Csv("license_date"),
                        Csv("delete_date"),
                        Csv("birth_date"),
                        Csv("data_kubun")
                    ));

                    while (true)
                    {
                        int grc = jv.JVGets(ref objBuff, BUFSIZE, out string fileName);

                        if (grc == 0) break;

                        if (grc < 0)
                        {
                            Console.WriteLine("[NG] JVGets失敗 rc=" + grc + " file=" + fileName);
                            continue;
                        }

                        var bytes = (byte[])objBuff;
                        string str = (grc > 0 && grc <= bytes.Length)
                            ? sjis.GetString(bytes, 0, grc)
                            : sjis.GetString(bytes);

                        if (string.IsNullOrEmpty(str) || str.Length < 2)
                        {
                            Console.WriteLine("[NG] レコード長不足 file=" + fileName);
                            continue;
                        }

                        totalCount++;

                        string recId = str.Substring(0, 2);

                        if (totalCount <= 30)
                        {
                            Console.WriteLine("[INFO] recId=" + recId + " file=" + fileName);
                        }

                        if (recId != "KS")
                            continue;

                        ksCount++;

                        string dataKubun = NullIfBlank(Sub1(str, 3, 1));
                        string jockeyCode = NullIfBlank(Sub1(str, 12, 5));
                        string deletedFlag = NullIfBlank(Sub1(str, 17, 1));
                        string licenseDate = NullIfBlank(Sub1(str, 18, 8));
                        string deleteDate = NullIfBlank(Sub1(str, 26, 8));
                        string birthDate = NullIfBlank(Sub1(str, 34, 8));
                        string jockeyName = NullIfBlank(Sub1(str, 42, 34));
                        string jockeyNameShort = NullIfBlank(Sub1(str, 140, 8));
                        string belongCode = NullIfBlank(Sub1(str, 231, 1));
                        string inviteRegionName = NullIfBlank(Sub1(str, 232, 20));
                        string belongTrainerCode = ZeroToNull(NullIfBlank(Sub1(str, 252, 5)));
                        string belongTrainerNameShort = NullIfBlank(Sub1(str, 257, 8));

                        sw.WriteLine(string.Join(",",
                            Csv(jockeyCode),
                            Csv(jockeyName),
                            Csv(jockeyNameShort),
                            Csv(belongCode),
                            Csv(BelongName(belongCode)),
                            Csv(inviteRegionName),
                            Csv(belongTrainerCode),
                            Csv(belongTrainerNameShort),
                            Csv(deletedFlag),
                            Csv(licenseDate),
                            Csv(deleteDate),
                            Csv(birthDate),
                            Csv(dataKubun)
                        ));

                        if (ksCount <= 20)
                        {
                            Console.WriteLine(
                                "[KS] code=" + ToNullLiteral(jockeyCode) +
                                " name=" + ToNullLiteral(jockeyName) +
                                " belong=" + ToNullLiteral(belongCode) +
                                " trainerCode=" + ToNullLiteral(belongTrainerCode) +
                                " trainerName=" + ToNullLiteral(belongTrainerNameShort)
                            );
                        }
                    }
                }

                Console.WriteLine("done: " + outCsv);
                Console.WriteLine("totalCount=" + totalCount + ", KS count=" + ksCount);

                try { jv.JVClose(); } catch { }
            }

            static string Sub1(string s, int start1Based, int len)
            {
                if (string.IsNullOrEmpty(s)) return "";
                int start = start1Based - 1;
                if (start >= s.Length) return "";
                if (start + len > s.Length) len = s.Length - start;
                return s.Substring(start, len).Trim();
            }

            static string NullIfBlank(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                return s.Trim();
            }

            static string ZeroToNull(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                string t = s.Trim();

                bool allZero = true;
                for (int i = 0; i < t.Length; i++)
                {
                    if (t[i] != '0')
                    {
                        allZero = false;
                        break;
                    }
                }

                return allZero ? "" : t;
            }

            static string BelongName(string code)
            {
                switch ((code ?? "").Trim())
                {
                    case "1": return "美浦";
                    case "2": return "栗東";
                    case "3": return "地方招待";
                    case "4": return "外国招待";
                    default: return "";
                }
            }

            static string Csv(string s)
            {
                if (s == null) s = "";
                s = s.Replace("\"", "\"\"");
                return "\"" + s + "\"";
            }

            static string ToNullLiteral(string s)
            {
                return string.IsNullOrWhiteSpace(s) ? "NULL" : s;
            }
        }
        Thread.Sleep(200);
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"JVRead rc={rc}");
                        break;
                    }
                }

                try { jv.JVClose(); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR");
                Console.WriteLine(ex.ToString());
                try { jv.JVClose(); } catch { }
            }

            Console.WriteLine("Press Enter...");
            Console.ReadLine();
        }
    }
}