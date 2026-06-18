using System.IO;

namespace SatisfactorySaveParser
{
    /// <summary>
    ///     Engine class: FObjectSaveHeader
    /// </summary>
    public class SaveComponent : SaveObject
    {
        public const int TypeID = 0;

        /// <summary>
        ///     Instance name of the parent entity object
        /// </summary>
        public string ParentEntityName { get; set; }

        public SaveComponent(string typePath, string rootObject, string instanceName) : base(typePath, rootObject, instanceName)
        {
        }

        public SaveComponent(BinaryReader reader) : base(reader)
        {
            ParentEntityName = reader.ReadLengthPrefixedString();
        }

        /// <summary>
        ///     1.0+ TOC blob からオブジェクトヘッダー（FObjectSaveHeader）を読む。
        ///     ベースで ClassName/Reference を読み、ObjectFlags（SaveVersion>=49）→ OuterPathName の順。
        /// </summary>
        public SaveComponent(BinaryReader reader, Save.FSaveCustomVersion saveVersion) : base(reader)
        {
            if (saveVersion >= Save.FSaveCustomVersion.AddedObjectFlagsToHeader)
                ObjectFlags = reader.ReadUInt32();

            ParentEntityName = reader.ReadLengthPrefixedString(); // OuterPathName
        }

        public override void SerializeHeader(BinaryWriter writer)
        {
            base.SerializeHeader(writer);

            writer.WriteLengthPrefixedString(ParentEntityName);
        }

        public override void SerializeNewHeader(BinaryWriter writer, Save.FSaveCustomVersion saveVersion)
        {
            base.SerializeNewHeader(writer, saveVersion);

            writer.WriteLengthPrefixedString(ParentEntityName); // OuterPathName
        }

        public override string ToString()
        {
            return TypePath;
        }
    }
}
