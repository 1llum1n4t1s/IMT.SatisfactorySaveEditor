using SuperLightLogger;
using SatisfactorySaveParser.PropertyTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SatisfactorySaveParser
{
    public class SerializedFields : List<SerializedProperty>
    {
        private static readonly ILog log = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Used to handle edge cases where objects have 0 bytes of data, and we don't want to generate a "None" string either
        /// </summary>
        public bool ShouldBeNulled { get; private set; } = false;

        public byte[] TrailingData { get; set; }

        public void Serialize(BinaryWriter writer, int buildVersion)
        {
            if (ShouldBeNulled && Count == 0 && TrailingData.Length == 0)
                return;

            foreach (var field in this)
            {
                field.Serialize(writer, buildVersion);
            }

            writer.WriteLengthPrefixedString("None");

            writer.Write(0);
            if (TrailingData != null)
                writer.Write(TrailingData);
        }

        public static SerializedFields Parse(int length, BinaryReader reader, int buildVersion)
        {
            var start = reader.BaseStream.Position;
            var result = new SerializedFields();

            if (length == 0)
            {
                log.Warn($"Tried to parse 0 byte object data @ {start}");
                result.ShouldBeNulled = true;
                return result;
            }

            SerializedProperty prop;
            while ((prop = SerializedProperty.Parse(reader, buildVersion)) != null)
            {
                result.Add(prop);
            }

            var int1 = reader.ReadInt32();
            Trace.Assert(int1 == 0);

            var remainingBytes = start + length - reader.BaseStream.Position;
            if (remainingBytes > 0)
            {
                //log.Warn($"{remainingBytes} bytes left after reading all serialized fields!");
                result.TrailingData = reader.ReadBytes((int)remainingBytes);
                //log.Trace(BitConverter.ToString(result.TrailingData).Replace("-", " "));
            }

            //if (remainingBytes == 4)
            ////if(result.Fields.Count > 0)
            //{
            //    var int2 = reader.ReadInt32();
            //}
            //else if (remainingBytes > 0 && result.Any(f => f is ArrayProperty && ((ArrayProperty)f).Type == StructProperty.TypeName))
            //{
            //    var unk = reader.ReadBytes((int)remainingBytes);
            //}
            //else if (remainingBytes > 4)
            //{
            //    var int2 = reader.ReadInt32();
            //    var str2 = reader.ReadLengthPrefixedString();
            //    var str3 = reader.ReadLengthPrefixedString();
            //}


            return result;
        }

        /// <summary>このフィールド列を直列化→再パースして完全な deep copy を作る。複製時に原本と
        /// 要素（SerializedProperty）/ ShouldBeNulled / TrailingData を共有せず、片方の編集が他方へ波及しない。</summary>
        public SerializedFields DeepClone(int buildVersion)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, new System.Text.UTF8Encoding(false), true))
                    Serialize(w, buildVersion);
                ms.Position = 0;
                using (var r = new BinaryReader(ms))
                    return Parse((int)ms.Length, r, buildVersion);
            }
        }
    }
}
