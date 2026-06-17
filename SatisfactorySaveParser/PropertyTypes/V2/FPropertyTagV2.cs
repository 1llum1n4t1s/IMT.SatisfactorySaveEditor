using System.IO;

namespace SatisfactorySaveParser.PropertyTypes.V2
{
    /// <summary>
    ///     Satisfactory 1.0+ の PropertyTag。詳細仕様は docs/SAVE_FORMAT_1.0.md §6 参照。
    /// </summary>
    public class FPropertyTagV2
    {
        public const byte FlagHasIndex = 0x01;
        public const byte FlagHasGuid = 0x02;
        public const byte FlagBoolValue = 0x10;

        public string Name { get; set; }
        public FPropertyTagNodeV2 TagType { get; set; }
        public int BinarySize { get; set; }
        public byte Flags { get; set; }
        public int Index { get; set; }
        public byte[] PropertyGuid { get; set; }   // 16 byte / null when bit 0x02 is off

        public bool IsTerminator => Name == "None";
        public bool HasIndex => (Flags & FlagHasIndex) != 0;
        public bool HasGuid => (Flags & FlagHasGuid) != 0;
        public bool BoolTagValue => (Flags & FlagBoolValue) != 0;

        public static FPropertyTagV2 Read(BinaryReader reader)
        {
            var tag = new FPropertyTagV2
            {
                Name = reader.ReadLengthPrefixedString()
            };

            // "None" タグはここで終わる（プロパティリスト終端）
            if (tag.Name == "None")
                return tag;

            tag.TagType = FPropertyTagNodeV2.Read(reader);
            tag.BinarySize = reader.ReadInt32();
            tag.Flags = reader.ReadByte();

            if (tag.HasIndex)
                tag.Index = reader.ReadInt32();

            if (tag.HasGuid)
            {
                tag.PropertyGuid = reader.ReadBytes(16);
                if (tag.PropertyGuid.Length != 16)
                    throw new InvalidDataException($"PropertyTag '{tag.Name}' truncated: expected 16-byte GUID, got {tag.PropertyGuid.Length}");
            }

            return tag;
        }

        public void Write(BinaryWriter writer)
        {
            writer.WriteLengthPrefixedString(Name);
            if (IsTerminator) return;

            TagType.Write(writer);
            writer.Write(BinarySize);
            writer.Write(Flags);
            if (HasIndex) writer.Write(Index);
            if (HasGuid) writer.Write(PropertyGuid);
        }

        public override string ToString()
        {
            return $"{Name}: {TagType} ({BinarySize}B, flags=0x{Flags:X2}{(HasIndex ? ", idx=" + Index : "")})";
        }
    }
}
