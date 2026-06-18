using SatisfactorySaveParser.Structures;
using System.Collections.Generic;
using System.IO;

namespace SatisfactorySaveParser
{
    /// <summary>
    ///     Engine class: FActorSaveHeader
    /// </summary>
    public class SaveEntity : SaveObject
    {
        public const int TypeID = 1;

        /// <summary>
        ///     Unknown use
        /// </summary>
        public bool NeedTransform { get; set; }

        /// <summary>
        ///     Rotation in the world
        /// </summary>
        public Vector4 Rotation { get; set; }

        /// <summary>
        ///     Position in the world
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        ///     Scale in the world
        /// </summary>
        public Vector3 Scale { get; set; }

        /// <summary>
        ///     Unknown use
        /// </summary>
        public bool WasPlacedInLevel { get; set; }

        /// <summary>
        ///     Unknown related (parent?) object root
        /// </summary>
        public string ParentObjectRoot { get; set; }
        /// <summary>
        /// Unknown related (parent?) object name
        /// </summary>
        public string ParentObjectName { get; set; }

        /// <summary>
        ///     List of SaveComponents belonging to this object
        /// </summary>
        public List<ObjectReference> Components { get; set; } = new List<ObjectReference>();

        /// <summary>
        ///     このアクターが他オブジェクトへの参照（親アクター参照・コンポーネント参照・プロパティ内 ObjectProperty）を
        ///     持つかを判定する。1.0+ の raw アクターは内部データを <see cref="SaveObject.RawData"/> に不透明保持し
        ///     <see cref="Components"/> が未展開のため、data プレフィックス（parentRoot / parentName / componentCount。
        ///     docs/SAVE_FORMAT_1.0.md §7）を直接読んで判定する。RawData は読み取るだけで書き換えないため round-trip は不変。
        ///     componentCount==0 でも電線（PowerLine）等は両端ポールへの参照を専用データ側に持ち標準枠に収まらないので、
        ///     プロパティリストが "None" 終端まで読める（誤読でない）こと＋ ObjectProperty を含まないことの二重で確認する。
        ///     レガシー（RawData==null・展開済み）は <see cref="ParentObjectName"/> / <see cref="Components"/> で判定する。
        ///     複製可否の止血用: 参照を持つアクターの複製は参照グラフを二重化してセーブを破損させる。
        /// </summary>
        public bool HasOutgoingReferences()
        {
            if (RawData == null)
                return !string.IsNullOrEmpty(ParentObjectName) || (Components != null && Components.Count > 0);

            try
            {
                int skip;
                using (var stream = new MemoryStream(RawData, false))
                using (var reader = new BinaryReader(stream))
                {
                    reader.ReadLengthPrefixedString();                  // parentRoot（空なら 4byte）
                    var parentName = reader.ReadLengthPrefixedString(); // parentName（空なら 4byte）
                    if (!string.IsNullOrEmpty(parentName))
                        return true;                                    // 親アクターへの参照あり

                    var componentCount = reader.ReadInt32();
                    // 先頭構造の仮定が外れて誤位置を読むと componentCount が異常値になる。各 component は最低 8byte
                    //（FString×2、空でも 4+4）なので、RawData に収まり得る上限を超えたら構造を信頼せず安全側で拒否する。
                    if (componentCount < 0 || (long)componentCount * 8 > RawData.Length)
                        return true;
                    if (componentCount > 0)
                        return true;                                    // コンポーネント参照あり
                    skip = (int)stream.Position + 1; // components 0個 + parent なし Actor の 1byte 追加プレフィクス
                }

                if (skip > RawData.Length)
                    return true;
                var sub = new byte[RawData.Length - skip];
                System.Array.Copy(RawData, skip, sub, 0, sub.Length);
                var props = PropertyTypes.V2.PropertiesListV2.Parse(sub); // "None" 未到達なら例外 → catch → true
                foreach (var p in props.Properties)
                {
                    if (TagTreeHasObjectProperty(p.Tag?.TagType))
                        return true;                                    // プロパティ内の他オブジェクト参照あり
                }
                return false;                                           // 参照を持たない → 安全に複製できる
            }
            catch
            {
                // data プレフィックスやプロパティを読めない異常時は安全側（参照ありとみなして複製を拒否）。
                return true;
            }
        }

        /// <summary>型名ツリー（FPropertyTypeName）を再帰走査し ObjectProperty を含むか判定する。
        /// ArrayProperty&lt;ObjectProperty&gt; 等のネストした参照（電線の両端ポール参照など）も拾う。</summary>
        private static bool TagTreeHasObjectProperty(PropertyTypes.V2.FPropertyTagNodeV2 node)
        {
            if (node == null) return false;
            if (node.Name == "ObjectProperty") return true;
            foreach (var c in node.Children)
                if (TagTreeHasObjectProperty(c)) return true;
            return false;
        }

        public SaveEntity(string typePath, string rootObject, string instanceName) : base(typePath, rootObject, instanceName)
        {
        }

        public SaveEntity(BinaryReader reader) : base(reader)
        {
            NeedTransform = reader.ReadInt32() == 1;
            Rotation = reader.ReadVector4();
            Position = reader.ReadVector3();
            Scale = reader.ReadVector3();
            WasPlacedInLevel = reader.ReadInt32() == 1;
        }

        /// <summary>
        ///     1.0+ TOC blob からアクターヘッダー（FActorSaveHeader）を読む。
        ///     ベースで ClassName/Reference を読み、ObjectFlags（SaveVersion>=49）→ トランスフォームの順。
        /// </summary>
        public SaveEntity(BinaryReader reader, Save.FSaveCustomVersion saveVersion) : base(reader)
        {
            if (saveVersion >= Save.FSaveCustomVersion.AddedObjectFlagsToHeader)
                ObjectFlags = reader.ReadUInt32();

            NeedTransform = reader.ReadInt32() == 1;
            Rotation = reader.ReadVector4();
            Position = reader.ReadVector3();
            Scale = reader.ReadVector3();
            WasPlacedInLevel = reader.ReadInt32() == 1;
        }

        public override void SerializeNewHeader(BinaryWriter writer, Save.FSaveCustomVersion saveVersion)
        {
            base.SerializeNewHeader(writer, saveVersion);

            writer.Write(NeedTransform ? 1 : 0);
            writer.Write(Rotation);
            writer.Write(Position);
            writer.Write(Scale);
            writer.Write(WasPlacedInLevel ? 1 : 0);
        }

        public override void SerializeHeader(BinaryWriter writer)
        {
            base.SerializeHeader(writer);

            writer.Write(NeedTransform ? 1 : 0);
            writer.Write(Rotation);
            writer.Write(Position);
            writer.Write(Scale);
            writer.Write(WasPlacedInLevel ? 1 : 0);
        }

        public override void SerializeData(BinaryWriter writer, int buildVersion)
        {
            writer.WriteLengthPrefixedString(ParentObjectRoot);
            writer.WriteLengthPrefixedString(ParentObjectName);

            writer.Write(Components.Count);
            foreach (var obj in Components)
            {
                writer.WriteLengthPrefixedString(obj.LevelName);
                writer.WriteLengthPrefixedString(obj.PathName);
            }

            base.SerializeData(writer, buildVersion);
        }

        public override void ParseData(int length, BinaryReader reader, int buildVersion)
        {
            var newLen = length - 12;
            ParentObjectRoot = reader.ReadLengthPrefixedString();
            if (ParentObjectRoot.Length > 0)
                newLen -= ParentObjectRoot.Length + 1;

            ParentObjectName = reader.ReadLengthPrefixedString();
            if (ParentObjectName.Length > 0)
                newLen -= ParentObjectName.Length + 1;

            var componentCount = reader.ReadInt32();
            for (int i = 0; i < componentCount; i++)
            {
                var componentRef = new ObjectReference(reader);
                Components.Add(componentRef);
                newLen -= 10 + componentRef.LevelName.Length + componentRef.PathName.Length;
            }

            base.ParseData(newLen, reader, buildVersion);
        }
    }
}
