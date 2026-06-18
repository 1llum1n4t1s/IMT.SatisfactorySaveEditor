using System.Collections.Generic;
using System.IO;

namespace SatisfactorySaveParser.PropertyTypes.V2
{
    /// <summary>
    ///     Satisfactory 1.0+ オブジェクトの内部プロパティ列。タグだけ読んで本体はバイト保持する Phase 3a 実装。
    /// </summary>
    public class PropertiesListV2
    {
        public List<PropertyV2> Properties { get; set; } = new List<PropertyV2>();

        /// <summary>"None" 終端後に残った末尾バイト（既存形式と同様に round-trip 用に保持）。</summary>
        public byte[] Trailing { get; set; }

        /// <summary>
        ///     プロパティ列をパースする。len バイト分を消費し切る。"None" 終端後の余りは Trailing に格納。
        ///     失敗時は例外を投げる（呼び出し側で握り、オブジェクト単位の RawData フォールバックへ）。
        /// </summary>
        public static PropertiesListV2 Parse(byte[] data)
        {
            var result = new PropertiesListV2();
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                while (ms.Position < ms.Length)
                {
                    var tag = FPropertyTagV2.Read(reader);
                    if (tag.IsTerminator)
                    {
                        // None タグ以降は trailing として保持（既存形式との parity）
                        var remaining = (int)(ms.Length - ms.Position);
                        result.Trailing = remaining > 0 ? reader.ReadBytes(remaining) : new byte[0];
                        return result;
                    }

                    if (tag.BinarySize < 0 || ms.Position + tag.BinarySize > ms.Length)
                        throw new InvalidDataException($"PropertyTag '{tag.Name}' size {tag.BinarySize} would overrun blob at {ms.Position}");

                    var body = reader.ReadBytes(tag.BinarySize);
                    result.Properties.Add(new PropertyV2 { Tag = tag, Body = body });
                }

                // None 終端なしで EOF に到達 = 形式違反
                throw new InvalidDataException("Property stream ended without 'None' terminator");
            }
        }
    }
}
