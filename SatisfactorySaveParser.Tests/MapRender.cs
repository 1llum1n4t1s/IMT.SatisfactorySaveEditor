using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SatisfactorySaveParser;

namespace SatisfactorySaveParser.Tests
{
    /// <summary>
    ///     実セーブの設置済みアクターを上から見た配置図 PNG にレンダリングする（表示専用・セーブ非改変）。
    ///     System.Drawing(Windows GDI+)。カテゴリ別カラー・日本語凡例・スケールバーを焼き込む。
    /// </summary>
    [TestClass]
    public class MapRender
    {
        private const string OutPath = @"C:\Users\IMT\AppData\Local\Temp\factory_map.png";

        private static readonly string[] CatNames =
        {
            "その他", "コンベア", "電力", "パイプ", "鉄道", "生産",
            "採取", "発電", "貯蔵", "構造", "看板・照明", "特殊建築"
        };

        // カテゴリ別カラー（暗背景で映える）
        private static readonly Color[] Pal =
        {
            Color.FromArgb(150, 160, 170), // 0 その他
            Color.FromArgb( 74, 163, 255), // 1 コンベア（青）
            Color.FromArgb(255, 210,  63), // 2 電力（黄）
            Color.FromArgb( 46, 196, 182), // 3 パイプ（teal）
            Color.FromArgb(176, 125,  72), // 4 鉄道（茶）
            Color.FromArgb(255,  77,  79), // 5 生産（赤）
            Color.FromArgb(199, 125, 255), // 6 採取（紫）
            Color.FromArgb(255, 159,  28), // 7 発電（橙）
            Color.FromArgb( 46, 204, 113), // 8 貯蔵（緑）
            Color.FromArgb(120, 132, 140), // 9 構造（灰）
            Color.FromArgb(210, 196, 150), // 10 看板・照明（淡）
            Color.FromArgb(255,  94, 162), // 11 特殊建築（桃）
        };

        // 描画順（雑多を先に、目立たせたい重要カテゴリを後＝上に）
        private static readonly int[] DrawOrder = { 9, 10, 1, 3, 2, 0, 4, 8, 6, 7, 5, 11 };
        private static bool Important(int c) => c == 4 || c == 5 || c == 6 || c == 7 || c == 8 || c == 11;

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
            bool C(string sub) => seg.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
            if (C("Conveyor")) return 1;
            if (C("PowerLine") || C("PowerPole") || C("PowerTower") || C("PowerSwitch") || C("PowerStorage")) return 2;
            if (C("Pipeline") || C("PipeHyper") || C("Hypertube") || C("HyperTube") || C("Valve") || C("PipeStorage")) return 3;
            if (C("Railroad") || C("Train")) return 4;
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
        public void RenderMap()
        {
            var sample = FindSample();
            Assert.IsNotNull(sample, "サンプルが見つからない");
            var save = new SatisfactorySave(sample);

            // 設置物を収集 + 範囲計算
            var pts = new System.Collections.Generic.List<(float x, float y, int c)>(80000);
            var counts = new int[CatNames.Length];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var obj in save.Entries)
            {
                if (!(obj is SaveEntity ent)) continue;
                var p = ent.Position;
                if (float.IsNaN(p.X) || float.IsNaN(p.Y)) continue;
                if (Math.Abs(p.X) < 1f && Math.Abs(p.Y) < 1f) continue;
                if (Math.Abs(p.X) > 1e7f || Math.Abs(p.Y) > 1e7f) continue;
                var seg = obj.TypePath ?? "";
                var slash = seg.LastIndexOf('/'); if (slash >= 0) seg = seg.Substring(slash + 1);
                var dot = seg.LastIndexOf('.'); if (dot >= 0) seg = seg.Substring(dot + 1);
                int cat = CategoryOf(seg);
                counts[cat]++;
                pts.Add((p.X, p.Y, cat));
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }

            float spanX = maxX - minX, spanY = maxY - minY;

            // レイアウト
            int marginL = 48, marginR = 48, marginT = 86, marginB = 64;
            int plotW = 1560, plotH = (int)Math.Round(plotW * spanY / spanX);
            int imgW = plotW + marginL + marginR;
            int imgH = plotH + marginT + marginB;
            float scale = Math.Min(plotW / spanX, plotH / spanY);
            float offX = marginL + (plotW - spanX * scale) / 2f;
            float offY = marginT + (plotH - spanY * scale) / 2f;

            PointF W2S(float wx, float wy) => new PointF(offX + (wx - minX) * scale, offY + (wy - minY) * scale);

            using (var bmp = new Bitmap(imgW, imgH, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(14, 17, 22));

                // 枠 + 1km グリッド
                using (var grid = new Pen(Color.FromArgb(40, 255, 255, 255)))
                using (var axis = new Pen(Color.FromArgb(90, 255, 255, 255)))
                {
                    int km = 100000; // 1km = 100000cm
                    int gx0 = (int)Math.Ceiling(minX / km) * km;
                    for (int wx = gx0; wx <= maxX; wx += km)
                    {
                        var a = W2S(wx, minY); var b = W2S(wx, maxY);
                        g.DrawLine(Math.Abs(wx) < 1 ? axis : grid, a.X, a.Y, b.X, b.Y);
                    }
                    int gy0 = (int)Math.Ceiling(minY / km) * km;
                    for (int wy = gy0; wy <= maxY; wy += km)
                    {
                        var a = W2S(minX, wy); var b = W2S(maxX, wy);
                        g.DrawLine(Math.Abs(wy) < 1 ? axis : grid, a.X, a.Y, b.X, b.Y);
                    }
                    g.DrawRectangle(new Pen(Color.FromArgb(80, 255, 255, 255)), marginL, marginT, plotW, plotH);
                }

                // 点描画（雑多→重要）
                foreach (var cat in DrawOrder)
                {
                    using (var br = new SolidBrush(Pal[cat]))
                    {
                        float r = Important(cat) ? 3.0f : 1.4f;
                        float d = r * 2;
                        foreach (var pt in pts)
                        {
                            if (pt.c != cat) continue;
                            var s = W2S(pt.x, pt.y);
                            g.FillRectangle(br, s.X - r, s.Y - r, d, d);
                        }
                    }
                }

                // タイトル
                using (var fT = new Font("Yu Gothic UI", 22, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fS = new Font("Yu Gothic UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var wbr = new SolidBrush(Color.FromArgb(235, 240, 245)))
                using (var sbr = new SolidBrush(Color.FromArgb(150, 160, 170)))
                {
                    g.DrawString("くれぱす — 工場配置図（上から）", fT, wbr, marginL, 18);
                    var session = Path.GetFileNameWithoutExtension(sample);
                    g.DrawString($"設置物 {pts.Count:N0} 件 ／ 範囲 {spanX / 100000:0.0} × {spanY / 100000:0.0} km ／ 北が上",
                                 fS, sbr, marginL, 50);

                    // 方位「北」
                    g.DrawString("北 ↑", fS, sbr, marginL + 6, marginT + 6);
                }

                // スケールバー（1km）
                using (var fS = new Font("Yu Gothic UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var wbr = new SolidBrush(Color.FromArgb(220, 225, 230)))
                using (var bar = new Pen(Color.FromArgb(220, 225, 230), 2f))
                {
                    float barLen = 100000 * scale; // 1km
                    float bx = marginL + 14, by = imgH - 28;
                    g.DrawLine(bar, bx, by, bx + barLen, by);
                    g.DrawLine(bar, bx, by - 5, bx, by + 5);
                    g.DrawLine(bar, bx + barLen, by - 5, bx + barLen, by + 5);
                    g.DrawString("1 km", fS, wbr, bx + barLen + 8, by - 9);
                }

                // 凡例（右上オーバーレイ）
                using (var fL = new Font("Yu Gothic UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var lbr = new SolidBrush(Color.FromArgb(225, 230, 235)))
                {
                    int lw = 196, lh = 24 * CatNames.Length + 16;
                    int lx = imgW - marginR - lw - 6, ly = marginT + 6;
                    using (var pan = new SolidBrush(Color.FromArgb(170, 20, 24, 30)))
                        g.FillRectangle(pan, lx, ly, lw, lh);
                    g.DrawRectangle(new Pen(Color.FromArgb(70, 255, 255, 255)), lx, ly, lw, lh);
                    for (int i = 0; i < CatNames.Length; i++)
                    {
                        int yy = ly + 10 + i * 24;
                        using (var sw = new SolidBrush(Pal[i]))
                            g.FillRectangle(sw, lx + 10, yy + 2, 14, 14);
                        g.DrawString($"{CatNames[i]}  {counts[i]:N0}", fL, lbr, lx + 32, yy);
                    }
                }

                bmp.Save(OutPath, ImageFormat.Png);
            }

            Assert.IsTrue(File.Exists(OutPath));
        }
    }
}
