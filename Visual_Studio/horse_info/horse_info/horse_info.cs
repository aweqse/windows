using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JVDTLabLib;
using static JVData_Struct;

namespace JvHorseInfoExport
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            /*
             * 目的：
             * 2010年以降に生まれた競走馬マスタ UM を、horse_id重複なしでCSV出力する。
             *
             * 引数:
             * args[0] = 生年月日From YYYYMMDD、省略時 20100101
             * args[1] = 生年月日To   YYYYMMDD、省略時 0
             * args[2] = JV取得開始日時 YYYYMMDDHHMMSS、省略時 19860101000000
             * args[3] = JVOpen option、省略時 4
             *
             * 例:
             * 2010年以降生まれを全部:
             *   JvHorseInfoExport.exe
             *
             * 2010年〜2024年生まれ:
             *   JvHorseInfoExport.exe 20100101 20241231
             *
             * 2021年生まれだけ:
             *   JvHorseInfoExport.exe 20210101 20211231
             */

            string birthFromText = args.Length >= 1 ? args[0] : "20100101";
            string birthToText = args.Length >= 2 ? args[1] : "0";
            string jvFromTime = args.Length >= 3 ? args[2] : "19860101000000";

            int openOption = 4;
            if (args.Length >= 4)
            {
                int parsedOption;
                if (int.TryParse(args[3], out parsedOption))
                {
                    openOption = parsedOption;
                }
            }

            int birthFrom = ToIntYmd(birthFromText);
            int birthTo = ToIntYmd(birthToText);

            string baseDir = @"C:\Users\dev-w\Desktop\workspace\output\horse_csv";

            string fileSuffix =
                (birthFrom == 0 ? "all" : birthFrom.ToString()) +
                "_" +
                (birthTo == 0 ? "latest" : birthTo.ToString());

            string outCsv = Path.Combine(baseDir, "horse_info_" + fileSuffix + ".csv");

            JVLinkClass jv = new JVLinkClass();

            Dictionary<string, HorseInfoRow> horseMap = new Dictionary<string, HorseInfoRow>();

            int totalRecordCount = 0;
            int totalUmCount = 0;
            int emptyHorseIdCount = 0;
            int birthdayUnknownCount = 0;
            int birthFilteredCount = 0;
            int duplicateCount = 0;
            int replacedCount = 0;
            int skippedOldCount = 0;

            try
            {
                Directory.CreateDirectory(baseDir);
                Console.OutputEncoding = Encoding.UTF8;

                Console.WriteLine("=== JRA-VAN horse_info export ===");
                Console.WriteLine("DataSpec          : DIFN");
                Console.WriteLine("JV fromTime       : " + jvFromTime);
                Console.WriteLine("JV open option    : " + openOption);
                Console.WriteLine("Birth From        : " + (birthFrom == 0 ? "指定なし" : birthFrom.ToString()));
                Console.WriteLine("Birth To          : " + (birthTo == 0 ? "指定なし" : birthTo.ToString()));
                Console.WriteLine("Output CSV        : " + outCsv);
                Console.WriteLine();

                int rc = jv.JVInit("UNKNOWN");
                Console.WriteLine("JVInit rc=" + rc);

                if (rc != 0)
                {
                    Console.WriteLine("JVInit failed.");
                    return;
                }

                int readCount = 0;
                int downloadCount = 0;
                string lastFileTimestamp;

                /*
                 * 重要：
                 * option=1 ではなく、初回全件取得目的なので option=4 を使う。
                 * 4 はダイアログなしセットアップ取得。
                 */
                rc = jv.JVOpen("DIFN", jvFromTime, openOption, readCount, downloadCount, out lastFileTimestamp);

                Console.WriteLine("JVOpen(DIFN) rc=" + rc +
                                  ", read=" + readCount +
                                  ", dl=" + downloadCount +
                                  ", lastTs=" + lastFileTimestamp);

                if (rc < 0)
                {
                    Console.WriteLine("JVOpen failed. rc=" + rc);
                    return;
                }

                Encoding sjis = Encoding.GetEncoding("shift_jis");

                int buffSize = 120000;
                object objBuff = new byte[buffSize];

                while (true)
                {
                    string fileName;
                    int ret = jv.JVGets(ref objBuff, buffSize, out fileName);

                    if (ret == 0)
                    {
                        break;
                    }

                    if (ret == -1 || ret == -3)
                    {
                        continue;
                    }

                    if (ret < 0)
                    {
                        Console.WriteLine("JVGets error ret=" + ret);
                        break;
                    }

                    byte[] bytes = objBuff as byte[];
                    if (bytes == null || ret < 2)
                    {
                        continue;
                    }

                    totalRecordCount++;

                    string buff = sjis.GetString(bytes, 0, ret).TrimEnd('\0');

                    if (string.IsNullOrEmpty(buff) || buff.Length < 2)
                    {
                        continue;
                    }

                    string recId = buff.Substring(0, 2);

                    // 競走馬マスタ UM だけ処理する
                    if (recId != "UM")
                    {
                        continue;
                    }

                    totalUmCount++;

                    JV_UM_UMA um = new JV_UM_UMA();
                    um.SetDataB(ref buff);

                    HorseInfoRow row = CreateHorseInfoRow(um);

                    if (string.IsNullOrEmpty(row.HorseId))
                    {
                        emptyHorseIdCount++;
                        continue;
                    }

                    if (row.Birthday == 0)
                    {
                        birthdayUnknownCount++;
                        continue;
                    }

                    // 生年月日で絞り込み
                    if (!IsWithinYmdRange(row.Birthday, birthFrom, birthTo))
                    {
                        birthFilteredCount++;
                        continue;
                    }

                    if (!horseMap.ContainsKey(row.HorseId))
                    {
                        horseMap.Add(row.HorseId, row);
                    }
                    else
                    {
                        duplicateCount++;

                        HorseInfoRow oldRow = horseMap[row.HorseId];

                        if (IsNewerOrSame(row, oldRow))
                        {
                            horseMap[row.HorseId] = row;
                            replacedCount++;
                        }
                        else
                        {
                            skippedOldCount++;
                        }
                    }
                }

                jv.JVClose();

                WriteCsv(outCsv, horseMap);

                Console.WriteLine();
                Console.WriteLine("=== done ===");
                Console.WriteLine("Output CSV                  : " + outCsv);
                Console.WriteLine("Total record read            : " + totalRecordCount);
                Console.WriteLine("UM read count                : " + totalUmCount);
                Console.WriteLine("Empty horse_id skipped       : " + emptyHorseIdCount);
                Console.WriteLine("Birthday unknown skipped     : " + birthdayUnknownCount);
                Console.WriteLine("Birthday filtered            : " + birthFilteredCount);
                Console.WriteLine("Unique horse count           : " + horseMap.Count);
                Console.WriteLine("Duplicate horse_id count     : " + duplicateCount);
                Console.WriteLine("Replaced newer/same          : " + replacedCount);
                Console.WriteLine("Skipped older                : " + skippedOldCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());

                try
                {
                    jv.JVClose();
                }
                catch
                {
                }
            }
        }

        static HorseInfoRow CreateHorseInfoRow(JV_UM_UMA um)
        {
            string horseId = Clean(um.KettoNum);
            string horseName = Clean(um.Bamei);
            string sexCd = Clean(um.SexCD);

            int birthday = ToYmdInt(
                Clean(um.BirthDate.Year),
                Clean(um.BirthDate.Month),
                Clean(um.BirthDate.Day)
            );

            string trainerRaw = Clean(um.ChokyosiRyakusyo);
            string trainerId = Clean(um.ChokyosiCode);

            string breedRaw = Clean(um.BreederName);
            string breedId = Clean(um.BreederCode);

            string ownerRaw = Clean(um.BanusiName);
            string ownerCode = Clean(um.BanusiCode);

            int registJraDate = ToYmdInt(
                Clean(um.RegDate.Year),
                Clean(um.RegDate.Month),
                Clean(um.RegDate.Day)
            );

            int rejectJraDate = ToYmdInt(
                Clean(um.DelDate.Year),
                Clean(um.DelDate.Month),
                Clean(um.DelDate.Day)
            );

            string tozaiCd = Clean(um.TozaiCD);

            string fatherRaw = "";
            string fatherCode = "";
            string motherRaw = "";
            string motherCode = "";

            if (um.Ketto3Info != null && um.Ketto3Info.Length > 0)
            {
                fatherRaw = Clean(um.Ketto3Info[0].Bamei);
                fatherCode = Clean(um.Ketto3Info[0].HansyokuNum);
            }

            if (um.Ketto3Info != null && um.Ketto3Info.Length > 1)
            {
                motherRaw = Clean(um.Ketto3Info[1].Bamei);
                motherCode = Clean(um.Ketto3Info[1].HansyokuNum);
            }

            int lastUpdate = ToYmdInt(
                Clean(um.head.MakeDate.Year),
                Clean(um.head.MakeDate.Month),
                Clean(um.head.MakeDate.Day)
            );

            return new HorseInfoRow
            {
                HorseId = horseId,
                HorseName = horseName,
                SexCd = sexCd,
                Birthday = birthday,
                TrainerRaw = trainerRaw,
                TrainerId = trainerId,
                BreedRaw = breedRaw,
                BreedId = breedId,
                OwnerRaw = ownerRaw,
                OwnerCode = ownerCode,
                RegistJraDate = registJraDate,
                RejectJraDate = rejectJraDate,
                TozaiCd = tozaiCd,
                FatherRaw = fatherRaw,
                FatherCode = fatherCode,
                MotherRaw = motherRaw,
                MotherCode = motherCode,
                LastUpdate = lastUpdate
            };
        }

        static bool IsNewerOrSame(HorseInfoRow newRow, HorseInfoRow oldRow)
        {
            if (newRow.LastUpdate > oldRow.LastUpdate)
            {
                return true;
            }

            if (newRow.LastUpdate < oldRow.LastUpdate)
            {
                return false;
            }

            /*
             * 同じlast_updateなら後から読んだ方を優先。
             * 差分・訂正データでは後続データの方が新しい可能性があるため。
             */
            return true;
        }

        static void WriteCsv(string outCsv, Dictionary<string, HorseInfoRow> horseMap)
        {
            using (StreamWriter sw = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
            {
                sw.WriteLine(string.Join(",",
                    "horse_id",
                    "horse_name",
                    "sex_cd",
                    "birthday",
                    "trainer_raw",
                    "trainer_id",
                    "breed_raw",
                    "breed_id",
                    "owner_raw",
                    "owner_code",
                    "regist_jra_date",
                    "reject_jra_date",
                    "tozai_cd",
                    "father_raw",
                    "father_code",
                    "mother_raw",
                    "mother_code",
                    "last_update"
                ));

                foreach (KeyValuePair<string, HorseInfoRow> pair in horseMap.OrderBy(x => x.Key))
                {
                    HorseInfoRow row = pair.Value;

                    string[] cols =
                    {
                        Csv(row.HorseId),
                        Csv(row.HorseName),
                        Csv(row.SexCd),
                        Csv(row.Birthday == 0 ? "" : row.Birthday.ToString()),
                        Csv(row.TrainerRaw),
                        Csv(row.TrainerId),
                        Csv(row.BreedRaw),
                        Csv(row.BreedId),
                        Csv(row.OwnerRaw),
                        Csv(row.OwnerCode),
                        Csv(row.RegistJraDate == 0 ? "" : row.RegistJraDate.ToString()),
                        Csv(row.RejectJraDate == 0 ? "" : row.RejectJraDate.ToString()),
                        Csv(row.TozaiCd),
                        Csv(row.FatherRaw),
                        Csv(row.FatherCode),
                        Csv(row.MotherRaw),
                        Csv(row.MotherCode),
                        Csv(row.LastUpdate == 0 ? "" : row.LastUpdate.ToString())
                    };

                    sw.WriteLine(string.Join(",", cols));
                }
            }
        }

        static bool IsWithinYmdRange(int ymd, int fromYmd, int toYmd)
        {
            if (ymd == 0)
            {
                return false;
            }

            if (fromYmd != 0 && ymd < fromYmd)
            {
                return false;
            }

            if (toYmd != 0 && ymd > toYmd)
            {
                return false;
            }

            return true;
        }

        static int ToIntYmd(string ymd)
        {
            if (IsZeroLike(ymd))
            {
                return 0;
            }

            string s = (ymd ?? "").Trim();

            if (s.Length != 8)
            {
                return 0;
            }

            int value;
            if (int.TryParse(s, out value))
            {
                return value;
            }

            return 0;
        }

        static bool IsZeroLike(string s)
        {
            s = (s ?? "").Trim();

            return s == "" ||
                   s == "0" ||
                   s == "00000000" ||
                   s == "00000000000000";
        }

        static string Clean(string s)
        {
            if (s == null)
            {
                return "";
            }

            return s.Trim();
        }

        static string Csv(string s)
        {
            if (s == null)
            {
                s = "";
            }

            if (s.Contains("\""))
            {
                s = s.Replace("\"", "\"\"");
            }

            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s + "\"";
            }

            return s;
        }

        static int ToYmdInt(string y, string m, string d)
        {
            y = (y ?? "").Trim();
            m = (m ?? "").Trim();
            d = (d ?? "").Trim();

            if (string.IsNullOrEmpty(y) || string.IsNullOrEmpty(m) || string.IsNullOrEmpty(d))
            {
                return 0;
            }

            if (y == "0000" || m == "00" || d == "00")
            {
                return 0;
            }

            int value;
            string ymd = y.PadLeft(4, '0') + m.PadLeft(2, '0') + d.PadLeft(2, '0');

            if (int.TryParse(ymd, out value))
            {
                return value;
            }

            return 0;
        }
    }

    internal class HorseInfoRow
    {
        public string HorseId { get; set; }
        public string HorseName { get; set; }
        public string SexCd { get; set; }
        public int Birthday { get; set; }
        public string TrainerRaw { get; set; }
        public string TrainerId { get; set; }
        public string BreedRaw { get; set; }
        public string BreedId { get; set; }
        public string OwnerRaw { get; set; }
        public string OwnerCode { get; set; }
        public int RegistJraDate { get; set; }
        public int RejectJraDate { get; set; }
        public string TozaiCd { get; set; }
        public string FatherRaw { get; set; }
        public string FatherCode { get; set; }
        public string MotherRaw { get; set; }
        public string MotherCode { get; set; }
        public int LastUpdate { get; set; }
    }
}