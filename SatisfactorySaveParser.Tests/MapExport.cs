using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SatisfactorySaveParser;

namespace SatisfactorySaveParser.Tests
{
    /// <summary>
    ///     実セーブから配置図用に「設置済みアクターの XY 座標 + カテゴリ」を抽出して JSON 出力する（表示専用・安全）。
    ///     プロパティ解析には一切触れないため round-trip にもメモリにも影響しない。
    /// </summary>
    [TestClass]
    public class MapExport
    {
        private const string OutPath = @"C:\Users\IMT\AppData\Local\Temp\map_points.json";
        private const string CountPath = @"C:\Users\IMT\AppData\Local\Temp\map_counts.txt";

        private static readonly string[] CatNames =
        {
            "その他", "コンベア", "電力", "パイプ", "鉄道", "生産",
            "採取", "発電", "貯蔵", "構造", "看板・照明", "特殊建築"
        };

        private static string FindSample()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!Directory.Exists(docs)) return null;
            var saves = Directory.GetFiles(docs, "*.sav");
            return saves.FirstOrDefault(f => f.Contains("160626-193833"))
                   ?? saves.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        private static int CategoryOf(string seg)
        {
            // seg = クラスパス末尾（例 Build_ConveyorBeltMk6_C）。出現頻度順に判定（生産→構造の取り違え回避）。
            bool C(string sub) => seg.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

            if (C("Conveyor")) return 1;
            if (C("PowerLine") || C("PowerPole") || C("PowerTower") || C("PowerSwitch") || C("PowerStorage")) return 2;
            if (C("Pipeline") || C("PipeHyper") || C("Hypertube") || C("HyperTube") || C("Valve") || C("PipeStorage")) return 3;
            if (C("Railroad") || C("Train") || C("RailroadTrack")) return 4;
            if (C("Constructor") || C("Assembler") || C("Manufacturer") || C("Smelter") || C("Foundry")
                || C("Refinery") || C("Blender") || C("Converter") || C("HadronCollider")
                || C("QuantumEncoder") || C("Packager") || C("Mam") || C("WorkBench") || C("Workshop")) return 5;
            if (C("Miner") || C("OilPump") || C("WaterPump") || C("Fracking") || C("ResourceExtractor")) return 6;
            if (C("Generator") || C("Nuclear") || C("AlienPower")) return 7;
            if (C("Storage") || C("IndustrialTank") || C("CentralStorage")) return 8;
            if (C("Foundation") || C("Wall") || C("Beam") || C("Frame") || C("Pillar") || C("Ramp")
                || C("Stair") || C("Roof") || C("Catwalk") || C("Walkway") || C("Ladder") || C("Gate")
                || C("Passthrough") || C("Fence")) return 9;
            if (C("Sign") || C("Light") || C("Floodlight") || C("StreetLight")) return 10;
            if (C("SpaceElevator") || C("TradingPost") || C("HubTerminal") || C("ResourceSink")
                || C("DroneStation") || C("TruckStation") || C("RadarTower") || C("Portal")
                || C("Drone") || C("Vehicle")) return 11;
            return 0;
        }

        [TestMethod]
        public void ExportPoints()
        {
            var sample = FindSample();
            Assert.IsNotNull(sample, "サンプルが見つからない");
            var save = new SatisfactorySave(sample);

            // 重要カテゴリは細かく(3m)、物量の多い雑多カテゴリは粗く(8m)デデュープ。座標出力はメートル単位。
            int QForCat(int cat)
            {
                switch (cat)
                {
                    case 4: case 5: case 6: case 7: case 8: case 11: return 300;  // 鉄道/生産/採取/発電/貯蔵/特殊（細かく）
                    default: return 1500;                                         // コンベア/電力/パイプ/構造/看板/その他（粗く）
                }
            }

            var counts = new int[CatNames.Length];      // デデュープ後のユニーク件数
            var rawCounts = new int[CatNames.Length];   // 元の設置数（凡例表示用）
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var xs = new List<int>();   // メートル単位
            var ys = new List<int>();
            var cs = new List<byte>();
            var seen = new HashSet<long>();

            foreach (var obj in save.Entries)
            {
                if (!(obj is SaveEntity ent)) continue;
                var p = ent.Position;
                // 設置済み（原点近傍の subsystem を除外）。null/NaN/極端値も弾く。
                if (p == null || float.IsNaN(p.X) || float.IsNaN(p.Y)) continue;
                if (Math.Abs(p.X) < 1f && Math.Abs(p.Y) < 1f) continue;
                if (Math.Abs(p.X) > 1e7f || Math.Abs(p.Y) > 1e7f) continue;

                var seg = obj.TypePath ?? "";
                var slash = seg.LastIndexOf('/'); if (slash >= 0) seg = seg.Substring(slash + 1);
                var dot = seg.LastIndexOf('.'); if (dot >= 0) seg = seg.Substring(dot + 1);

                int cat = CategoryOf(seg);
                rawCounts[cat]++;
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;

                int q = QForCat(cat);
                long key = ((long)((int)Math.Round(p.X / q) + 100000) << 36)
                         | ((long)((int)Math.Round(p.Y / q) + 100000) << 8) | (uint)cat;
                if (!seen.Add(key)) continue;

                counts[cat]++;
                xs.Add((int)Math.Round(p.X / 100f)); ys.Add((int)Math.Round(p.Y / 100f)); cs.Add((byte)cat);
            }

            int n = xs.Count;

            // 互いに素なフラット配列で JSON 出力（パートごと、軽量）。
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"n\":").Append(n).Append(',');
            sb.Append("\"unit\":\"m\",");
            sb.Append("\"bounds\":[")   // メートル単位
              .Append((int)Math.Round(minX / 100f)).Append(',').Append((int)Math.Round(minY / 100f)).Append(',')
              .Append((int)Math.Round(maxX / 100f)).Append(',').Append((int)Math.Round(maxY / 100f)).Append("],");
            sb.Append("\"cats\":[").Append(string.Join(",", CatNames.Select(c => "\"" + c + "\""))).Append("],");
            sb.Append("\"counts\":[").Append(string.Join(",", rawCounts)).Append("],");
            sb.Append("\"x\":[").Append(string.Join(",", xs)).Append("],");
            sb.Append("\"y\":[").Append(string.Join(",", ys)).Append("],");
            sb.Append("\"c\":[").Append(string.Join(",", cs.Select(b => (int)b))).Append(']');
            sb.Append('}');

            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));

            var rep = new StringBuilder();
            rep.AppendLine($"sample={sample}");
            rep.AppendLine($"placed entities raw={rawCounts.Sum()} unique(after per-cat dedup)={n}");
            rep.AppendLine($"bounds X[{(int)minX}..{(int)maxX}] Y[{(int)minY}..{(int)maxY}]");
            for (int i = 0; i < CatNames.Length; i++)
                rep.AppendLine($"{rawCounts[i],8} raw / {counts[i],7} uniq  [{i}] {CatNames[i]}");
            rep.AppendLine($"json bytes={new FileInfo(OutPath).Length}");
            File.WriteAllText(CountPath, rep.ToString(), new UTF8Encoding(false));
        }
    }
}
