using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SatisfactorySaveParser;

namespace SatisfactorySaveParser.Tests
{
    /// <summary>
    ///     SaveEntity.HasOutgoingReferences が「複製を許可」する Actor が、実セーブ上で本当に
    ///     他オブジェクト参照を持たない構造物だけであることを検証する非密閉ハーネス。
    ///     PowerLine（電線）等は両端ポールへの参照（mWireInstances）を標準の component 配列でなく
    ///     専用データ側に持つため、HasOutgoingReferences は (1) プロパティリストの "None" 整合確認 と
    ///     (2) ObjectProperty 検出 の二重防御で拒否する。これらが複製許可に漏れたら失敗させる。
    /// </summary>
    [TestClass]
    public class ActorReferenceFramingVerification
    {
        private static string FindSampleSave()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var saveRoot = Path.Combine(docs, "My Games", "FactoryGame", "Saved", "SaveGames");
            if (!Directory.Exists(saveRoot)) return null;
            return Directory.GetFiles(saveRoot, "*.sav", SearchOption.AllDirectories)
                .FirstOrDefault(f => new FileInfo(f).Length > 0);
        }

        private static string LastSeg(string typePath)
        {
            if (string.IsNullOrEmpty(typePath)) return typePath;
            var slash = typePath.LastIndexOf('/');
            return slash >= 0 ? typePath.Substring(slash + 1) : typePath;
        }

        [TestMethod]
        public void VerifyHasOutgoingReferencesOnRealSave()
        {
            var savePath = FindSampleSave();
            if (savePath == null)
                Assert.Inconclusive("No sample save found under Documents/My Games/FactoryGame");

            var save = new SatisfactorySave(savePath);
            var actors = save.Entries.OfType<SaveEntity>().ToList();

            int allowDup = 0, rejectDup = 0;
            var allowedClasses = new Dictionary<string, int>();
            foreach (var actor in actors)
            {
                if (actor.HasOutgoingReferences())
                {
                    rejectDup++;
                }
                else
                {
                    allowDup++;
                    var cls = LastSeg(actor.TypePath);
                    allowedClasses[cls] = (allowedClasses.TryGetValue(cls, out var n) ? n : 0) + 1;
                }
            }

            Console.WriteLine($"Total actors: {actors.Count}, allow dup: {allowDup}, reject dup: {rejectDup}");
            Console.WriteLine("Classes allowed to duplicate (should be reference-free structures only):");
            foreach (var kv in allowedClasses.OrderByDescending(k => k.Value).Take(50))
                Console.WriteLine($"  {kv.Value,6}  {kv.Key}");

            // 参照を持つことが既知のクラス（電線・配線）が複製許可に漏れていないことを要求する。
            var leaked = allowedClasses.Keys
                .Where(c => c.IndexOf("PowerLine", StringComparison.OrdinalIgnoreCase) >= 0
                         || c.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            Assert.AreEqual(0, leaked.Count,
                $"参照保持クラスが複製許可に漏れた: {string.Join(", ", leaked)}");
        }
    }
}
