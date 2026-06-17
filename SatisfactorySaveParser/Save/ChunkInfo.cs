using System.IO;

namespace SatisfactorySaveParser.Save
{
    public class ChunkInfo
    {
        public const long Magic = 0x9E2A83C1;
        public const int ChunkSize = 131072; // 128 KiB

        /// <summary>1.0+ 圧縮チャンクの magic 直後に入るマーカー（旧形式では magic int64 の上位語=0）。</summary>
        public const int NewFormatMarker = 0x22222222;

        /// <summary>1.0+ 圧縮チャンクのアルゴリズム識別子（3 = zlib）。</summary>
        public const byte CompressorZlib = 3;

        public long CompressedSize { get; set; }
        public long UncompressedSize { get; set; }
    }
}
