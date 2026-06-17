using SatisfactorySaveParser.Save;
using SatisfactorySaveParser.Structures;
using System.IO;
using System.Linq;
using System.Text;

namespace SatisfactorySaveParser
{
    public static class BinaryIOExtensions
    {
        public static char[] ReadCharArray(this BinaryReader reader)
        {
            var count = reader.ReadInt32();
            // 不正オフセット（1.0 フレーミング走査・3D 接続オーバーレイ等）で巨大な count を読んだとき、
            // ReadChars/ReadBytes が確保前に OOM/ハングしないよう残ストリーム長で上限検証する。
            // 正当な長さ前置文字列は必ずストリーム内に収まるのでこの分岐は発火せず、round-trip も不変。
            var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (count >= 0)
            {
                if (count > remaining)
                    throw new InvalidDataException($"String length {count} exceeds remaining stream {remaining}");
                return reader.ReadChars(count);
            }
            else
            {
                long byteCount = -(long)count * 2;
                if (byteCount > remaining)
                    throw new InvalidDataException($"UTF-16 string length {count} exceeds remaining stream {remaining}");
                var bytes = reader.ReadBytes((int)byteCount);
                return Encoding.Unicode.GetChars(bytes);
            }
        }

        public static string ReadLengthPrefixedString(this BinaryReader reader)
        {
            return new string(reader.ReadCharArray()).TrimEnd('\0');
        }

        public static void WriteLengthPrefixedString(this BinaryWriter writer, string str)
        {
            if (str == null || str.Length == 0)
            {
                writer.Write(0);
                return;
            }

            if (str.Any(c => c > 127))
            {
                var bytes = Encoding.Unicode.GetBytes(str);

                writer.Write(-(bytes.Length / 2 + 1));
                writer.Write(bytes);
                writer.Write((short)0);
            }
            else
            {
                var bytes = Encoding.ASCII.GetBytes(str);

                writer.Write(bytes.Length + 1);
                writer.Write(bytes);
                writer.Write((byte)0);
            }
        }

        public static int GetSerializedLength(this string str)
        {
            if (str == null || str.Length == 0) return 4;

            if (str.Any(c => c > 127))
            {
                return str.Length * 2 + 6;
            }

            return str.Length + 5;
        }

        public static Vector2 ReadVector2(this BinaryReader reader)
        {
            return new Vector2()
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
            };
        }

        public static void Write(this BinaryWriter writer, Vector2 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
        }

        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            return new Vector3()
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle()
            };
        }

        public static void Write(this BinaryWriter writer, Vector3 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
        }

        public static Vector4 ReadVector4(this BinaryReader reader)
        {
            return new Vector4()
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                W = reader.ReadSingle()
            };
        }

        public static void Write(this BinaryWriter writer, Vector4 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
            writer.Write(vec.W);
        }

        public static ChunkInfo ReadChunkInfo(this BinaryReader reader)
        {
            return new ChunkInfo()
            {
                CompressedSize = reader.ReadInt64(),
                UncompressedSize = reader.ReadInt64()
            };
        }

        public static void Write(this BinaryWriter writer, ChunkInfo info)
        {
            writer.Write(info.CompressedSize);
            writer.Write(info.UncompressedSize);
        }
    }
}
