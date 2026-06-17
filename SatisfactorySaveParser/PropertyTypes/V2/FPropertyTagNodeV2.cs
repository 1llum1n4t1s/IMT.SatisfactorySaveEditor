using System.Collections.Generic;
using System.IO;

namespace SatisfactorySaveParser.PropertyTypes.V2
{
    /// <summary>
    ///     Satisfactory 1.0+（UE5 PROPERTY_TAG_COMPLETE_TYPE_NAME 以降）の PropertyTag に圧縮された
    ///     複合型情報（FPropertyTypeName）。型名 + 子型の再帰ツリー。
    /// </summary>
    public class FPropertyTagNodeV2
    {
        public string Name { get; set; }
        public List<FPropertyTagNodeV2> Children { get; set; } = new List<FPropertyTagNodeV2>();

        public static FPropertyTagNodeV2 Read(BinaryReader reader)
        {
            var node = new FPropertyTagNodeV2
            {
                Name = reader.ReadLengthPrefixedString()
            };
            var count = reader.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"FPropertyTagNode '{node.Name}' child count {count} is negative");
            for (var i = 0; i < count; i++)
                node.Children.Add(Read(reader));
            return node;
        }

        public void Write(BinaryWriter writer)
        {
            writer.WriteLengthPrefixedString(Name);
            writer.Write(Children.Count);
            foreach (var c in Children)
                c.Write(writer);
        }

        public override string ToString()
        {
            if (Children.Count == 0) return Name;
            var inner = string.Join(",", Children);
            return $"{Name}<{inner}>";
        }
    }
}
