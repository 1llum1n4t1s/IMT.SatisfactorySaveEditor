using System.IO;

namespace SatisfactorySaveParser
{
    /// <summary>
    ///     Class representing a single saved object in a Satisfactory save
    ///     Engine class: FObjectBaseSaveHeader
    /// </summary>
    public abstract class SaveObject
    {
        /// <summary>
        ///     Forward slash separated path of the script/prefab of this object.
        ///     Can be an empty string.
        /// </summary>
        public string TypePath { get; set; }

        /// <summary>
        ///     Root object (?) of this object
        ///     Often some form of "Persistent_Level", can be an empty string
        /// </summary>
        public string RootObject { get; set; }

        /// <summary>
        ///     Unique (?) name of this object
        /// </summary>
        public string InstanceName { get; set; }

        /// <summary>
        ///     Main serialized data of the object
        /// </summary>
        public SerializedFields DataFields { get; set; }

        /// <summary>
        ///     1.0+ 新形式のオブジェクトフラグ（UE の RF_* フラグ。SaveVersion>=AddedObjectFlagsToHeader）。
        /// </summary>
        public uint ObjectFlags { get; set; }

        /// <summary>1.0+ DATA blob の per-object SaveVersion。</summary>
        public int DataSaveVersion { get; set; }

        /// <summary>1.0+ DATA blob の ShouldMigrateObjectRefsToPersistent。</summary>
        public bool ShouldMigrate { get; set; }

        /// <summary>
        ///     1.0+ per-object の TOptional&lt;FSaveObjectVersionData&gt;（生バイト）。未設定なら null。
        /// </summary>
        public byte[] OptionalVersionData { get; set; }

        /// <summary>
        ///     1.0+ オブジェクト内部データ（プロパティ列）を未パースのまま保持する不透明バイト列。
        ///     第2段階ではこれをそのまま書き戻して round-trip を保証する（プロパティ編集は後段）。
        ///     非 null のとき DataFields より優先してシリアライズされる。
        /// </summary>
        public byte[] RawData { get; set; }

        public SaveObject(string typePath, string rootObject, string instanceName)
        {
            TypePath = typePath;
            RootObject = rootObject;
            InstanceName = instanceName;
        }

        protected SaveObject(BinaryReader reader)
        {
            TypePath = reader.ReadLengthPrefixedString();
            RootObject = reader.ReadLengthPrefixedString();
            InstanceName = reader.ReadLengthPrefixedString();
        }

        public virtual void SerializeHeader(BinaryWriter writer)
        {
            writer.WriteLengthPrefixedString(TypePath);
            writer.WriteLengthPrefixedString(RootObject);
            writer.WriteLengthPrefixedString(InstanceName);
        }

        /// <summary>
        ///     1.0+ TOC blob 用のヘッダーを書き出す。共通部（ClassName=TypePath / Reference=RootObject+InstanceName /
        ///     ObjectFlags）を書き、派生クラスが固有部（トランスフォーム / OuterPathName）を追記する。
        /// </summary>
        public virtual void SerializeNewHeader(BinaryWriter writer, Save.FSaveCustomVersion saveVersion)
        {
            writer.WriteLengthPrefixedString(TypePath);
            writer.WriteLengthPrefixedString(RootObject);
            writer.WriteLengthPrefixedString(InstanceName);

            if (saveVersion >= Save.FSaveCustomVersion.AddedObjectFlagsToHeader)
                writer.Write(ObjectFlags);
        }

        public virtual void SerializeData(BinaryWriter writer, int buildVersion)
        {
            DataFields.Serialize(writer, buildVersion);
        }

        public virtual void ParseData(int length, BinaryReader reader, int buildVersion)
        {
            DataFields = SerializedFields.Parse(length, reader, buildVersion);
        }

        public override string ToString()
        {
            return TypePath;
        }
    }
}
