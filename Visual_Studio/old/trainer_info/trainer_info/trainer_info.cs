using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using JVDTLabLib;
using static JVData_Struct;

namespace JvTrainerExport
{
    internal class Program
    {
        static readonly string OutputDir = @"C:\Users\dev-w\Desktop\workspace\Visual_Studio\output";
        static readonly string OutputCsv = Path.Combine(OutputDir, "trainer_info.csv");

        static readonly HashSet<string> JraPlaces = new HashSet<string>
        {
            "01","02","03","04","05","06","07","08","09","10"
        };

        class TrainerRow
        {
            public string trainer_id;
            public string trainer_name;
            public string belong_area;
            public string belong_update;
            public string active;
            public string license_delete_date;

            // 内部用
            public string latest_seen_date;
        }

        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string fromTime = args.Length >= 1 ? args[0] : "20240101000000";
            int option = 4;

            Directory.CreateDirectory(OutputDir);

            if (!Is14Digits(fromTime))
            {
                Console.WriteLine("fromTime が不正です: " + fromTime);
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

            int rc = jv.JVInit("TrainerExport");
            Console.WriteLine("JVInit rc=" + rc);
            if (rc != 0)
            {
                Console.ReadLine();
                return;
            }

            var trainerMap = BuildTrainerMapFromRace(jv, fromTime, option);
            Console.WriteLine("RACE base trainer count = " + trainerMap.Count);

            MergeLicenseDeleteFromCk(jv, fromTime, option, trainerMap);

            WriteTrainerCsv(OutputCsv, trainerMap);

            Console.WriteLine("done: " + OutputCsv);
            Console.ReadLine();
        }

        static Dictionary<string, TrainerRow> BuildTrainerMapFromRace(dynamic jv, string fromTime, int option)
        {
            int readCount = 0;
            int downloadCount = 0;
            string lastTs = "";

            int rc = jv.JVOpen("RACE", fromTime, option, ref readCount, ref downloadCount, ref lastTs);
            Console.WriteLine("JVOpen(RACE) rc=" + rc + " read=" + readCount + " dl=" + downloadCount + " last=" + lastTs);

            var trainerMap = new Dictionary<string, TrainerRow>(StringComparer.Ordinal);

            if (rc != 0)
            {
                jv.JVClose();
                return trainerMap;
            }

            object objBuff = Array.Empty<byte>();
            const int BUFSIZE = 110000;
            var sjis = Encoding.GetEncoding("shift_jis");

            JV_RA_RACE ra = new JV_RA_RACE();
            JV_SE_RACE_UMA se = new JV_SE_RACE_UMA();

            var raceDateMap = new Dictionary<string, string>(StringComparer.Ordinal);

            int loopCount = 0;
            int raCount = 0;
            int seCount = 0;
            int seErrorCount = 0;

            while (true)
            {
                loopCount++;

                int grc;
                string fileName;

                try
                {
                    grc = jv.JVGets(ref objBuff, BUFSIZE, out fileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RACE JVGets Exception: " + ex.Message);
                    break;
                }

                if (grc == 0) break;

                if (grc < 0)
                {
                    Console.WriteLine("RACE JVGets error rc=" + grc + " file=" + fileName);
                    continue;
                }

                byte[] bytes = (byte[])objBuff;
                string record = (grc > 0 && grc <= bytes.Length)
                    ? sjis.GetString(bytes, 0, grc)
                    : sjis.GetString(bytes);

                if (record.Length < 2) continue;

                string recId = record.Substring(0, 2);

                if (loopCount <= 5 || loopCount % 10000 == 0)
                {
                    Console.WriteLine("RACE loop=" + loopCount + " recId=" + recId + " file=" + fileName);
                }

                if (recId == "RA")
                {
                    raCount++;

                    try
                    {
                        ra.SetDataB(ref record);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("RA SetDataB error loop=" + loopCount + " file=" + fileName + " msg=" + ex.Message);
                        continue;
                    }

                    string year = GetFieldValue(ra, "Year");
                    string monthDay = GetFieldValue(ra, "MonthDay");
                    string jyoCd = Pad2(GetFieldValue(ra, "JyoCD"));
                    string kaiji = Pad2(GetFieldValue(ra, "Kaiji"));
                    string nichiji = Pad2(GetFieldValue(ra, "Nichiji"));
                    string raceNum = Pad2(GetFieldValue(ra, "RaceNum"));

                    if (!IsJraPlace(jyoCd)) continue;

                    string raceId = MakeRaceId(year, jyoCd, kaiji, nichiji, raceNum);
                    if (raceId.Length == 0) continue;

                    string ymd = Norm(year) + Norm(monthDay);
                    if (ymd.Length == 8)
                    {
                        raceDateMap[raceId] = ymd;
                    }
                }
                else if (recId == "SE")
                {
                    seCount++;

                    try
                    {
                        se.SetDataB(ref record);
                    }
                    catch (Exception ex)
                    {
                        seErrorCount++;
                        Console.WriteLine("SE SetDataB error loop=" + loopCount + " file=" + fileName + " msg=" + ex.Message);
                        continue;
                    }

                    string dataKubun = GetFieldValue(se, "DataKubun");
                    if (Norm(dataKubun) == "0") continue;

                    string year = GetFieldValue(se, "Year");
                    string jyoCd = Pad2(GetFieldValue(se, "JyoCD"));
                    string kaiji = Pad2(GetFieldValue(se, "Kaiji"));
                    string nichiji = Pad2(GetFieldValue(se, "Nichiji"));
                    string raceNum = Pad2(GetFieldValue(se, "RaceNum"));

                    if (!IsJraPlace(jyoCd)) continue;

                    string raceId = MakeRaceId(year, jyoCd, kaiji, nichiji, raceNum);
                    if (raceId.Length == 0) continue;

                    string trainerId = FirstNonEmpty(
                        GetFieldValue(se, "ChokyosiCode"),
                        GetFieldValue(se, "ChokyosiCD"),
                        GetFieldValue(se, "ChokyosiCd"),
                        GetFieldValue(se, "TrainerCode"),
                        GetFieldValue(se, "TrainerCD")
                    );

                    string trainerName = FirstNonEmpty(
                        GetFieldValue(se, "ChokyosiRyakusyo"),
                        GetFieldValue(se, "ChokyosiName"),
                        GetFieldValue(se, "TrainerRyakusyo"),
                        GetFieldValue(se, "TrainerName")
                    );

                    string belongArea = MapBelongArea(FirstNonEmpty(
                        GetFieldValue(se, "TozaiCD"),
                        GetFieldValue(se, "Tozai")
                    ));

                    if (trainerId.Length == 0 || trainerName.Length == 0)
                    {
                        continue;
                    }

                    string seenDate = raceDateMap.TryGetValue(raceId, out var ymd) ? ymd : "";

                    if (!trainerMap.TryGetValue(trainerId, out var row))
                    {
                        row = new TrainerRow
                        {
                            trainer_id = trainerId,
                            trainer_name = trainerName,
                            belong_area = belongArea,
                            belong_update = "",
                            active = "1",
                            license_delete_date = "",
                            latest_seen_date = seenDate
                        };
                        trainerMap[trainerId] = row;
                    }
                    else
                    {
                        if (row.trainer_name.Length == 0 && trainerName.Length != 0)
                        {
                            row.trainer_name = trainerName;
                        }

                        if (row.belong_area.Length == 0 && belongArea.Length != 0)
                        {
                            row.belong_area = belongArea;
                        }
                        else if (belongArea.Length != 0 && row.belong_area != belongArea)
                        {
                            if (CompareYmd(seenDate, row.latest_seen_date) >= 0)
                            {
                                row.belong_area = belongArea;
                            }
                        }

                        if (CompareYmd(seenDate, row.latest_seen_date) > 0)
                        {
                            row.latest_seen_date = seenDate;
                        }
                    }
                }
            }

            Console.WriteLine("RACE end loop=" + loopCount + " RA=" + raCount + " SE=" + seCount + " SEerr=" + seErrorCount + " trainer=" + trainerMap.Count);

            jv.JVClose();
            return trainerMap;
        }

        static void MergeLicenseDeleteFromCk(dynamic jv, string fromTime, int option, Dictionary<string, TrainerRow> trainerMap)
        {
            if (trainerMap.Count == 0)
            {
                Console.WriteLine("CK merge skip: trainerMap is empty");
                return;
            }

            int readCount = 0;
            int downloadCount = 0;
            string lastTs = "";

            int rc = jv.JVOpen("CK", fromTime, option, ref readCount, ref downloadCount, ref lastTs);
            Console.WriteLine("JVOpen(CK) rc=" + rc + " read=" + readCount + " dl=" + downloadCount + " last=" + lastTs);

            if (rc != 0)
            {
                jv.JVClose();
                return;
            }

            object objBuff = Array.Empty<byte>();
            const int BUFSIZE = 110000;
            var sjis = Encoding.GetEncoding("shift_jis");

            object ckParser = CreateParserByPreferredNames(
                "JV_CK_CYOKYOSI",
                "JV_CK_CHOKYOSI"
            );

            if (ckParser == null)
            {
                Console.WriteLine("CK parser が見つかりません。");
                jv.JVClose();
                return;
            }

            Console.WriteLine("CK parser type = " + ckParser.GetType().FullName);

            var targetIds = new HashSet<string>(trainerMap.Keys, StringComparer.Ordinal);
            var resolvedIds = new HashSet<string>(StringComparer.Ordinal);

            int loopCount = 0;
            int ckCount = 0;
            int ckErrorCount = 0;

            while (true)
            {
                loopCount++;

                if (resolvedIds.Count >= targetIds.Count)
                {
                    Console.WriteLine("CK early stop: resolved all target trainers");
                    break;
                }

                int grc;
                string fileName;

                try
                {
                    grc = jv.JVGets(ref objBuff, BUFSIZE, out fileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CK JVGets Exception: " + ex.Message);
                    break;
                }

                if (grc == 0) break;

                if (grc < 0)
                {
                    Console.WriteLine("CK JVGets error rc=" + grc + " file=" + fileName);
                    continue;
                }

                byte[] bytes = (byte[])objBuff;
                string record = (grc > 0 && grc <= bytes.Length)
                    ? sjis.GetString(bytes, 0, grc)
                    : sjis.GetString(bytes);

                if (record.Length < 2) continue;

                string recId = record.Substring(0, 2);

                if (loopCount <= 5 || loopCount % 5000 == 0)
                {
                    Console.WriteLine("CK loop=" + loopCount + " recId=" + recId + " file=" + fileName + " resolved=" + resolvedIds.Count + "/" + targetIds.Count);
                }

                if (recId != "CK")
                {
                    continue;
                }

                try
                {
                    InvokeSetDataB(ckParser, ref record);
                }
                catch (Exception ex)
                {
                    ckErrorCount++;
                    Console.WriteLine("CK SetDataB error loop=" + loopCount + " file=" + fileName + " msg=" + ex.Message);
                    continue;
                }

                ckCount++;

                string trainerId = FirstNonEmpty(
                    GetFieldValue(ckParser, "ChokyosiCode"),
                    GetFieldValue(ckParser, "TrainerCode"),
                    GetFieldValue(ckParser, "ChokyosiCD"),
                    GetFieldValue(ckParser, "TrainerCD")
                );

                if (trainerId.Length == 0) continue;
                if (!targetIds.Contains(trainerId)) continue;

                string trainerName = FirstNonEmpty(
                    GetFieldValue(ckParser, "ChokyosiName"),
                    GetFieldValue(ckParser, "TrainerName"),
                    GetFieldValue(ckParser, "ChokyosiRyakuName"),
                    GetFieldValue(ckParser, "TrainerRyakuName"),
                    GetFieldValue(ckParser, "ChokyosiRyakusyo"),
                    GetFieldValue(ckParser, "TrainerRyakusyo")
                );

                string belongArea = MapBelongArea(FirstNonEmpty(
                    GetFieldValue(ckParser, "TozaiCD"),
                    GetFieldValue(ckParser, "Tozai")
                ));

                string licenseDeleteDate = FirstNonEmpty(
                    GetFieldValue(ckParser, "MenkyoMasshoDate"),
                    GetFieldValue(ckParser, "LicenseDeleteDate")
                );

                if (trainerMap.TryGetValue(trainerId, out var row))
                {
                    if (row.trainer_name.Length == 0 && trainerName.Length != 0)
                    {
                        row.trainer_name = trainerName;
                    }

                    if (row.belong_area.Length == 0 && belongArea.Length != 0)
                    {
                        row.belong_area = belongArea;
                    }

                    row.license_delete_date = licenseDeleteDate;
                    row.active = licenseDeleteDate.Length == 8 ? "0" : "1";
                }

                resolvedIds.Add(trainerId);
            }

            Console.WriteLine("CK end loop=" + loopCount + " CK=" + ckCount + " CKerr=" + ckErrorCount + " resolved=" + resolvedIds.Count + "/" + targetIds.Count);

            jv.JVClose();
        }

        static void WriteTrainerCsv(string outCsv, Dictionary<string, TrainerRow> trainerMap)
        {
            using (var sw = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("trainer_id,trainer_name,belong_area,belong_update,active,license_delete_date");

                foreach (var row in trainerMap.Values
                    .OrderBy(x => ToSortableNumber(x.trainer_id))
                    .ThenBy(x => x.trainer_id, StringComparer.Ordinal))
                {
                    sw.WriteLine(string.Join(",",
                        Csv(ToNullLiteral(row.trainer_id)),
                        Csv(ToNullLiteral(row.trainer_name)),
                        Csv(ToNullLiteral(row.belong_area)),
                        Csv(ToNullLiteral(row.belong_update)),
                        Csv(ToNullLiteral(row.active)),
                        Csv(ToNullLiteral(row.license_delete_date))
                    ));
                }
            }
        }

        static object CreateParserByPreferredNames(params string[] typeNames)
        {
            var asm = typeof(JV_RA_RACE).Assembly;
            var allTypes = asm.GetTypes();

            foreach (var name in typeNames)
            {
                var t = allTypes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.Ordinal));
                if (t != null) return Activator.CreateInstance(t);
            }

            var fallback = allTypes.FirstOrDefault(x => x.Name.StartsWith("JV_CK_", StringComparison.Ordinal));
            if (fallback != null) return Activator.CreateInstance(fallback);

            return null;
        }

        static void InvokeSetDataB(object parser, ref string s)
        {
            var mi = parser.GetType().GetMethod("SetDataB", BindingFlags.Public | BindingFlags.Instance);
            if (mi == null) throw new MissingMethodException(parser.GetType().FullName, "SetDataB");

            object[] args = new object[] { s };
            mi.Invoke(parser, args);
            s = args[0] == null ? "" : args[0].ToString();
        }

        static string GetFieldValue(object root, params string[] candidateNames)
        {
            if (root == null || candidateNames == null || candidateNames.Length == 0) return "";

            foreach (var name in candidateNames)
            {
                string v = GetFieldValueCore(root, name, 0, new HashSet<int>());
                if (v.Length != 0) return v;
            }

            return "";
        }

        static string GetFieldValueCore(object obj, string targetName, int depth, HashSet<int> visited)
        {
            if (obj == null) return "";
            if (depth > 8) return "";

            Type t = obj.GetType();

            if (IsLeafType(t))
            {
                return "";
            }

            if (!t.IsValueType && t != typeof(string))
            {
                int key = RuntimeHelpers.GetHashCode(obj);
                if (visited.Contains(key)) return "";
                visited.Add(key);
            }

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try
                {
                    v = f.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (f.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    string s = ToLeafString(v);
                    if (s.Length != 0) return s;
                }

                if (v != null)
                {
                    Type vt = v.GetType();
                    if (!IsLeafType(vt) && !vt.IsArray)
                    {
                        string nested = GetFieldValueCore(v, targetName, depth + 1, visited);
                        if (nested.Length != 0) return nested;
                    }
                }
            }

            return "";
        }

        static string ToLeafString(object v)
        {
            if (v == null) return "";

            Type t = v.GetType();

            if (t == typeof(string)) return Norm((string)v);
            if (t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(TimeSpan))
            {
                return Norm(v.ToString());
            }

            return "";
        }

        static bool IsLeafType(Type t)
        {
            if (t.IsPrimitive) return true;
            if (t.IsEnum) return true;
            if (t == typeof(string)) return true;
            if (t == typeof(decimal)) return true;
            if (t == typeof(DateTime)) return true;
            if (t == typeof(TimeSpan)) return true;
            return false;
        }

        static string MakeRaceId(string year, string jyo, string kaiji, string nichiji, string raceNum)
        {
            year = Norm(year);
            jyo = Pad2(jyo);
            kaiji = Pad2(kaiji);
            nichiji = Pad2(nichiji);
            raceNum = Pad2(raceNum);

            if (year.Length != 4 || jyo.Length != 2 || kaiji.Length != 2 || nichiji.Length != 2 || raceNum.Length != 2)
            {
                return "";
            }

            return year + jyo + kaiji + nichiji + raceNum;
        }

        static string MapBelongArea(string tozaiCd)
        {
            tozaiCd = Norm(tozaiCd);

            switch (tozaiCd)
            {
                case "1": return "0";
                case "2": return "1";
                case "3": return "2";
                case "4": return "3";
                default: return "";
            }
        }

        static int CompareYmd(string a, string b)
        {
            a = Norm(a);
            b = Norm(b);

            if (a.Length != 8 && b.Length != 8) return 0;
            if (a.Length == 8 && b.Length != 8) return 1;
            if (a.Length != 8 && b.Length == 8) return -1;

            return string.CompareOrdinal(a, b);
        }

        static long ToSortableNumber(string s)
        {
            s = Norm(s);
            if (long.TryParse(s, out long n)) return n;
            return long.MaxValue;
        }

        static bool IsJraPlace(string jyoCd)
        {
            return JraPlaces.Contains(Pad2(jyoCd));
        }

        static bool Is14Digits(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 14) return false;

            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }

            return true;
        }

        static string Csv(string s)
        {
            if (s == null) s = "";
            s = s.Replace("\r", " ").Replace("\n", " ");

            if (s == "NULL") return "NULL";

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string Norm(string s)
        {
            if (s == null) return "";
            return s.Trim(' ', '　');
        }

        static string ToNullLiteral(string s)
        {
            s = Norm(s);
            if (s.Length == 0) return "NULL";
            return s;
        }

        static string Pad2(string s)
        {
            s = Norm(s);
            if (s.Length == 0) return "";
            if (s.Length == 1) return "0" + s;
            return s;
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return "";

            for (int i = 0; i < values.Length; i++)
            {
                string s = Norm(values[i]);
                if (s.Length != 0) return s;
            }

            return "";
        }
    }
}