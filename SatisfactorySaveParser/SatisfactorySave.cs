using SuperLightLogger;
using SatisfactorySaveParser.Save;
using SatisfactorySaveParser.Structures;
using System.IO.Compression;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SatisfactorySaveParser
{
    /// <summary>
    ///     SatisfactorySave is the main class for parsing a savegame
    /// </summary>
    public class SatisfactorySave
    {
        private static readonly ILog log = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Path to save on disk
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        ///     Header part of the save containing things like the version and metadata
        /// </summary>
        public FSaveHeader Header { get; private set; }

        /// <summary>
        ///     Main content of the save game
        /// </summary>
        public List<SaveObject> Entries { get; set; } = new List<SaveObject>();

        /// <summary>
        ///     List of object references of all collected objects in the world (Nut/berry bushes, slugs, etc)
        /// </summary>
        public List<ObjectReference> CollectedObjects { get; set; } = new List<ObjectReference>();

        /// <summary>
        ///     Satisfactory 1.0+（ワールドパーティション）のボディ。新形式セーブのときのみ非 null。
        ///     旧 Update 5 までの平坦形式では null で、<see cref="Entries"/> を使う。
        /// </summary>
        public SaveBodyV2 BodyV2 { get; private set; }

        /// <summary>
        ///     Open a savefile from disk
        /// </summary>
        /// <param name="file">Full path to the .sav file, usually found in %localappdata%/FactoryGame/Saved/SaveGames</param>
        public SatisfactorySave(string file)
        {
            log.Info($"Opening save file: {file}");

            FileName = Environment.ExpandEnvironmentVariables(file);

            using (var stream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length == 0)
                {
                    throw new Exception("Save file is completely empty");
                }

                Header = FSaveHeader.Parse(reader);

                if (Header.SaveVersion < FSaveCustomVersion.SaveFileIsCompressed)
                {
                    LoadData(reader);
                }
                else
                {
                    using (var buffer = Decompress(stream, reader, Header.IsNewFormat))
                    {
                        buffer.Position = 0;

                        using (var bufferReader = new BinaryReader(buffer))
                        {
                            if (Header.IsNewFormat)
                            {
                                // 1.0+ ワールドパーティション形式。永続レベルのオブジェクトを Entries に展開する
                                BodyV2 = SaveBodyV2.Parse(bufferReader, Header);
                                Entries = BodyV2.PersistentObjects;
                            }
                            else
                            {
                                var dataLength = bufferReader.ReadInt32();
                                LoadData(bufferReader);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     圧縮チャンク列を展開して 1 本のメモリストリームに連結する。
        ///     旧形式（6×int64 ヘッダー）と 1.0+ 形式（マーカー 0x22222222 + アルゴリズム byte）に対応。
        /// </summary>
        private static MemoryStream Decompress(FileStream stream, BinaryReader reader, bool isNewFormat)
        {
            var buffer = new MemoryStream();

            while (stream.Position < stream.Length)
            {
                long compressedSize;

                if (isNewFormat)
                {
                    var magic = reader.ReadInt32();
                    if (magic != unchecked((int)ChunkInfo.Magic))
                        throw new InvalidDataException($"不正なチャンク magic 0x{magic:X8}（期待 0x{ChunkInfo.Magic:X8}）");
                    var marker = reader.ReadInt32();   // marker 0x22222222
                    if (marker != ChunkInfo.NewFormatMarker)
                        throw new InvalidDataException($"Unexpected chunk marker 0x{marker:X8} (expected 0x{ChunkInfo.NewFormatMarker:X8})");
                    reader.ReadInt64();   // maxChunkSize (131072)
                    var compressorAlgo = reader.ReadByte();    // compressor algorithm (3 = zlib)
                    if (compressorAlgo != ChunkInfo.CompressorZlib)
                        throw new InvalidDataException($"Unsupported chunk compressor algorithm {compressorAlgo} (expected {ChunkInfo.CompressorZlib} = zlib)");
                    compressedSize = reader.ReadInt64();
                    reader.ReadInt64();   // uncompressedSize
                    reader.ReadInt64();   // compressedSize (繰り返し)
                    reader.ReadInt64();   // uncompressedSize (繰り返し)
                }
                else
                {
                    var header = reader.ReadChunkInfo();
                    if (header.CompressedSize != ChunkInfo.Magic)
                        throw new InvalidDataException($"不正な旧形式チャンク magic 0x{header.CompressedSize:X}");
                    if (header.UncompressedSize != ChunkInfo.ChunkSize)
                        throw new InvalidDataException($"不正な旧形式チャンクサイズ {header.UncompressedSize}");

                    reader.ReadChunkInfo(); // summary

                    var subChunk = reader.ReadChunkInfo();
                    compressedSize = subChunk.CompressedSize;
                }

                // 破損/悪意あるファイルでの無限ループ・解凍爆弾を防ぐ: compressedSize は信頼境界外の値なので
                // 正値かつ残ストリーム内であることを検証してから、その長さ分だけ切り出して展開する。
                // 部分ストリームに対して展開すると ZLibStream の先読み越境も無くなり、入力位置の手動補正も不要になる。
                if (compressedSize <= 0 || stream.Position + compressedSize > stream.Length)
                    throw new InvalidDataException($"不正な圧縮チャンク長 {compressedSize}（残り {stream.Length - stream.Position} バイト）");

                var compressedChunk = reader.ReadBytes((int)compressedSize);
                using (var chunkStream = new MemoryStream(compressedChunk, writable: false))
                using (var zStream = new ZLibStream(chunkStream, CompressionMode.Decompress))
                {
                    zStream.CopyTo(buffer);
                }
            }

            return buffer;
        }

        private void LoadData(BinaryReader reader)
        {
            // Does not need to be a public property because it's equal to Entries.Count
            var totalSaveObjects = reader.ReadUInt32();
            log.Info($"Save contains {totalSaveObjects} object headers");

            // Saved entities loop
            for (int i = 0; i < totalSaveObjects; i++)
            {
                var type = reader.ReadInt32();
                switch (type)
                {
                    case SaveEntity.TypeID:
                        Entries.Add(new SaveEntity(reader));
                        break;
                    case SaveComponent.TypeID:
                        Entries.Add(new SaveComponent(reader));
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected type {type}");
                }
            }

            var totalSaveObjectData = reader.ReadInt32();
            log.Info($"Save contains {totalSaveObjectData} object data");
            if (Entries.Count != totalSaveObjects)
                throw new InvalidDataException($"オブジェクトヘッダー数の不一致: {Entries.Count} != {totalSaveObjects}");
            if (Entries.Count != totalSaveObjectData)
                throw new InvalidDataException($"オブジェクトデータ数の不一致: {Entries.Count} != {totalSaveObjectData}");

            for (int i = 0; i < Entries.Count; i++)
            {
                var len = reader.ReadInt32();
                var before = reader.BaseStream.Position;

                Entries[i].ParseData(len, reader, Header.BuildVersion);
                var after = reader.BaseStream.Position;

                if (before + len != after)
                {
                    throw new InvalidOperationException($"Expected {len} bytes read but got {after - before}");
                }
            }

            var collectedObjectsCount = reader.ReadInt32();
            log.Info($"Save contains {collectedObjectsCount} collected objects");
            for (int i = 0; i < collectedObjectsCount; i++)
            {
                CollectedObjects.Add(new ObjectReference(reader));
            }

            log.Debug($"Read {reader.BaseStream.Position} of total {reader.BaseStream.Length} bytes");
            Trace.Assert(reader.BaseStream.Position == reader.BaseStream.Length);
        }

        public void Save()
        {
            Save(FileName);
        }

        public void Save(string file)
        {
            log.Info($"Writing save file: {file}");

            FileName = Environment.ExpandEnvironmentVariables(file);

            // 直列化（特に 1.0 の BodyV2.Serialize は未対応オブジェクトで例外を投げ得る）を先に
            // メモリ上で完了させてから出力先ファイルを開く。途中で失敗してもファイルをまだ
            // truncate していないので、元のセーブを部分書き込みで破壊しない（データ損失防止）。
            using (var fileBuffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(fileBuffer, new System.Text.UTF8Encoding(false), leaveOpen: true))
                {
                    Header.Serialize(writer);

                    if (Header.SaveVersion < FSaveCustomVersion.SaveFileIsCompressed)
                    {
                        SaveData(writer, Header.BuildVersion);
                    }
                    else
                    {
                        using (var buffer = new MemoryStream())
                        using (var bufferWriter = new BinaryWriter(buffer))
                        {
                            if (Header.IsNewFormat)
                            {
                                // int64 長さ + 本体。永続オブジェクトは Entries（編集後の状態）から再構築する
                                BodyV2.Serialize(bufferWriter, Header, Entries);
                            }
                            else
                            {
                                bufferWriter.Write(0); // Placeholder size

                                SaveData(bufferWriter, Header.BuildVersion);

                                buffer.Position = 0;
                                bufferWriter.Write((int)buffer.Length - 4);
                            }

                            buffer.Position = 0;
                            Compress(writer, buffer, Header.IsNewFormat);
                        }
                    }
                }

                // ここまで例外なく到達 = 直列化成功。出力先を truncate（FileMode.Create）して一括書き込みする。
                fileBuffer.Position = 0;
                using (var stream = new FileStream(FileName, FileMode.Create, FileAccess.Write))
                {
                    fileBuffer.CopyTo(stream);
                }
            }
        }

        /// <summary>
        ///     展開済みボディを 128KiB ごとに zlib 圧縮してチャンク列として書き出す。
        /// </summary>
        private static void Compress(BinaryWriter writer, MemoryStream buffer, bool isNewFormat)
        {
            for (var i = 0; i < (int)Math.Ceiling((double)buffer.Length / ChunkInfo.ChunkSize); i++)
            {
                using (var zBuffer = new MemoryStream())
                {
                    var remaining = (int)Math.Min(ChunkInfo.ChunkSize, buffer.Length - (ChunkInfo.ChunkSize * i));

                    using (var zStream = new ZLibStream(zBuffer, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        // buffer は呼び出し側で new MemoryStream() 確定なので GetBuffer() で内部配列を直接渡し、
                        // チャンク毎の tmpBuf 確保（128KiB × チャンク数）を避ける。
                        zStream.Write(buffer.GetBuffer(), ChunkInfo.ChunkSize * i, remaining);
                    }

                    if (isNewFormat)
                    {
                        writer.Write(unchecked((int)ChunkInfo.Magic));  // 0x9E2A83C1
                        writer.Write(ChunkInfo.NewFormatMarker);        // 0x22222222
                        writer.Write((long)ChunkInfo.ChunkSize);        // maxChunkSize
                        writer.Write(ChunkInfo.CompressorZlib);         // 3 = zlib
                        writer.Write(zBuffer.Length);                   // compressedSize
                        writer.Write((long)remaining);                  // uncompressedSize
                        writer.Write(zBuffer.Length);                   // compressedSize (繰り返し)
                        writer.Write((long)remaining);                  // uncompressedSize (繰り返し)
                        writer.Write(zBuffer.GetBuffer(), 0, (int)zBuffer.Length);
                    }
                    else
                    {
                        writer.Write(new ChunkInfo()
                        {
                            CompressedSize = ChunkInfo.Magic,
                            UncompressedSize = ChunkInfo.ChunkSize
                        });

                        writer.Write(new ChunkInfo()
                        {
                            CompressedSize = zBuffer.Length,
                            UncompressedSize = remaining
                        });

                        writer.Write(new ChunkInfo()
                        {
                            CompressedSize = zBuffer.Length,
                            UncompressedSize = remaining
                        });

                        writer.Write(zBuffer.GetBuffer(), 0, (int)zBuffer.Length);
                    }
                }
            }
        }

        private void SaveData(BinaryWriter writer, int buildVersion)
        {
            writer.Write(Entries.Count);

            var entities = Entries.Where(e => e is SaveEntity).ToArray();
            for (var i = 0; i < entities.Length; i++)
            {
                writer.Write(SaveEntity.TypeID);
                entities[i].SerializeHeader(writer);
            }

            var components = Entries.Where(e => e is SaveComponent).ToArray();
            for (var i = 0; i < components.Length; i++)
            {
                writer.Write(SaveComponent.TypeID);
                components[i].SerializeHeader(writer);
            }

            writer.Write(entities.Length + components.Length);

            using (var ms = new MemoryStream())
            using (var dataWriter = new BinaryWriter(ms))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    entities[i].SerializeData(dataWriter, buildVersion);

                    var bytes = ms.ToArray();
                    writer.Write(bytes.Length);
                    writer.Write(bytes);

                    ms.SetLength(0);
                }
                for (var i = 0; i < components.Length; i++)
                {
                    components[i].SerializeData(dataWriter, buildVersion);

                    var bytes = ms.ToArray();
                    writer.Write(bytes.Length);
                    writer.Write(bytes);

                    ms.SetLength(0);
                }
            }

            writer.Write(CollectedObjects.Count);
            foreach (var collectedObject in CollectedObjects)
            {
                writer.WriteLengthPrefixedString(collectedObject.LevelName);
                writer.WriteLengthPrefixedString(collectedObject.PathName);
            }
        }
    }
}
