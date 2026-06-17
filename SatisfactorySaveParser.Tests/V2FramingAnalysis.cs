using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SatisfactorySaveParser;
using SatisfactorySaveParser.PropertyTypes.V2;

namespace SatisfactorySaveParser.Tests
{
    /// <summary>
    ///     実セーブに対する 1.0 プロパティのフレーミング解析ハーネス（Stage3-A）。
    ///     永続レベル全オブジェクトについて Actor/Component のヘッダ後 prefix を「最小スキップ探索」で解決し、
    ///     PropertiesListV2 で "None" まで完走 + タグ/本体/Trailing の再シリアライズが byte-exact になることを検証する。
    ///     さらに出現プロパティ型 / struct 型のヒストグラムを採取して Phase B（本体デコード）の対象集合を確定する。
    ///     何が起きても必ずレポートを書き出す（finally）。通常 CI では走らせない（実ファイル依存）。
    /// </summary>
    [TestClass]
    public class V2FramingAnalysis
    {
        private const string ReportPath = @"C:\Users\IMT\AppData\Local\Temp\v2_framing_report.txt";

        private static string FindSample()
        {
            // 日本語ファイル名をソースリテラルに置かない（BOM なし .cs の文字コード誤解釈を避ける）。
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            if (Directory.Exists(docs))
            {
                var saves = Directory.GetFiles(docs, "*.sav");
                // 1.0 サンプルは "160626-193833" を含む。なければ最大ファイル。
                var match = saves.FirstOrDefault(f => f.Contains("160626-193833"));
                if (match != null) return match;
                if (saves.Length > 0) return saves.OrderByDescending(f => new FileInfo(f).Length).First();
            }
            return null;
        }

        private static byte[] Reserialize(PropertiesListV2 list)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                foreach (var p in list.Properties)
                {
                    p.Tag.Write(bw);
                    if (p.Body != null) bw.Write(p.Body);
                }
                new FPropertyTagV2 { Name = "None" }.Write(bw);
                if (list.Trailing != null) bw.Write(list.Trailing);
                return ms.ToArray();
            }
        }

        private static bool TryParseAt(byte[] raw, int start, out PropertiesListV2 list)
        {
            list = null;
            if (start < 0 || start > raw.Length) return false;
            var slice = new byte[raw.Length - start];
            Array.Copy(raw, start, slice, 0, slice.Length);
            try
            {
                var parsed = PropertiesListV2.Parse(slice);
                var reser = Reserialize(parsed);
                if (reser.Length == slice.Length && reser.AsSpan().SequenceEqual(slice))
                {
                    list = parsed;
                    return true;
                }
            }
            catch { }
            return false;
        }

        [TestMethod]
        public void AnalyzeFraming()
        {
            var sb = new StringBuilder();
            try
            {
                var samplePath = FindSample();
                sb.AppendLine($"sample={samplePath ?? "<none found>"}");
                if (samplePath == null) { return; }

                var save = new SatisfactorySave(samplePath);
                sb.AppendLine($"entries={save.Entries.Count}");

                int total = 0, ok = 0, fail = 0, noRaw = 0;
                var skipStats = new Dictionary<string, Dictionary<int, int>>();
                var failSamples = new List<string>();
                var propTypeHist = new Dictionary<string, int>();   // tag.TagType.ToString() -> count
                var structHist = new Dictionary<string, int>();     // StructProperty の struct 型名 -> count

                foreach (var obj in save.Entries)
                {
                    total++;
                    var raw = obj.RawData;
                    if (raw == null) { noRaw++; continue; }

                    bool isActor = obj is SaveEntity;
                    bool isComp = obj is SaveComponent;
                    int basePos = 0;
                    bool hasParent = false;
                    int compCount = 0;

                    try
                    {
                        using (var ms = new MemoryStream(raw))
                        using (var reader = new BinaryReader(ms))
                        {
                            if (isComp)
                            {
                                basePos = 0;
                            }
                            else if (isActor)
                            {
                                var pRoot = reader.ReadLengthPrefixedString();
                                var pName = reader.ReadLengthPrefixedString();
                                hasParent = (pRoot != null && pRoot.Length > 0) || (pName != null && pName.Length > 0);
                                compCount = reader.ReadInt32();
                                if (compCount < 0 || compCount > 100000)
                                    throw new InvalidDataException($"compCount異常 {compCount}");
                                for (int i = 0; i < compCount; i++)
                                {
                                    reader.ReadLengthPrefixedString();
                                    reader.ReadLengthPrefixedString();
                                }
                                basePos = (int)ms.Position;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        fail++;
                        if (failSamples.Count < 30)
                            failSamples.Add($"[hdr-fail] {obj.GetType().Name} {obj.TypePath} :: {e.Message}");
                        continue;
                    }

                    int[] candidates = isComp
                        ? new[] { 1, 0, 2, 3, 4, 5, 6, 8 }
                        : (hasParent ? new[] { 3, 1, 2, 0, 4, 5, 6, 8 } : new[] { 1, 3, 0, 2, 4, 5, 6, 8 });

                    int chosen = -1;
                    PropertiesListV2 parsedList = null;
                    foreach (var s in candidates)
                    {
                        if (TryParseAt(raw, basePos + s, out parsedList))
                        {
                            chosen = s;
                            break;
                        }
                    }

                    if (chosen < 0)
                    {
                        fail++;
                        if (failSamples.Count < 30)
                        {
                            var n = Math.Min(24, raw.Length - basePos);
                            var head = n > 0 ? BitConverter.ToString(raw, basePos, n) : "";
                            failSamples.Add($"[prop-fail] {(isActor ? "Actor" : isComp ? "Comp" : "?")} parent={hasParent} comps={compCount} {obj.TypePath} basePos={basePos} head={head}");
                        }
                        continue;
                    }

                    ok++;
                    var key = isComp ? "Comp" : (isActor ? $"Actor|parent={hasParent}|comps={(compCount == 0 ? "0" : ">0")}" : "Other");
                    if (!skipStats.TryGetValue(key, out var m)) skipStats[key] = m = new Dictionary<int, int>();
                    m[chosen] = m.TryGetValue(chosen, out var c) ? c + 1 : 1;

                    // ヒストグラム採取
                    foreach (var p in parsedList.Properties)
                    {
                        var tn = p.Tag.TagType != null ? p.Tag.TagType.ToString() : "<null>";
                        propTypeHist[tn] = propTypeHist.TryGetValue(tn, out var pc) ? pc + 1 : 1;
                        if (p.Tag.TagType != null && p.Tag.TagType.Name == "StructProperty" && p.Tag.TagType.Children.Count > 0)
                        {
                            var st = p.Tag.TagType.Children[0].Name;
                            structHist[st] = structHist.TryGetValue(st, out var sc) ? sc + 1 : 1;
                        }
                    }
                }

                sb.AppendLine($"total={total} ok={ok} fail={fail} noRaw={noRaw}");
                sb.AppendLine("--- skip stats (category -> skipBytes:count) ---");
                foreach (var kv in skipStats.OrderBy(k => k.Key))
                {
                    var dist = string.Join(", ", kv.Value.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
                    sb.AppendLine($"{kv.Key,-34} {dist}");
                }
                sb.AppendLine("--- property type histogram (tagType -> count) ---");
                foreach (var kv in propTypeHist.OrderByDescending(k => k.Value))
                    sb.AppendLine($"{kv.Value,9}  {kv.Key}");
                sb.AppendLine("--- struct type histogram (structName -> count) ---");
                foreach (var kv in structHist.OrderByDescending(k => k.Value))
                    sb.AppendLine($"{kv.Value,9}  {kv.Key}");
                sb.AppendLine("--- fail samples ---");
                foreach (var f in failSamples) sb.AppendLine(f);
            }
            catch (Exception fatal)
            {
                sb.AppendLine("FATAL: " + fatal);
            }
            finally
            {
                File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            }
        }
    }
}
