using SatisfactorySaveParser.Structures;
using System.Collections.Generic;
using System.IO;

namespace SatisfactorySaveParser.Save
{
    /// <summary>
    ///     Satisfactory 1.0+（HeaderVersion>=AddedWorldPartitionAndHash）の展開後ボディ。
    ///     ワールドパーティション構造（FSaveObjectVersionData / 検証グリッド / per-level マップ /
    ///     永続ランタイム / 未解決）を保持する。
    ///
    ///     第1段階では各レベルの TOC/Data blob を生バイトのまま保持し、コンテナ枠を
    ///     バイト完全 round-trip させることを保証する（編集対象オブジェクトの展開は後段で行う）。
    ///     詳細仕様は docs/SAVE_FORMAT_1.0.md を参照。
    /// </summary>
    public class SaveBodyV2
    {
        /// <summary>サブレベル（ワールドパーティションのセル毎）1件分のデータ。</summary>
        public class PartitionLevel
        {
            public string Name { get; set; }
            public byte[] TocBlob { get; set; }
            public byte[] DataBlob { get; set; }
            public int SaveVersion { get; set; }
            public List<ObjectReference> DestroyedActors { get; set; } = new List<ObjectReference>();
            /// <summary>TOptional&lt;FSaveObjectVersionData&gt;。未設定なら null。</summary>
            public byte[] OptionalVersionData { get; set; }
        }

        /// <summary>FSaveObjectVersionData（ボディ先頭、SaveVersion>=53）の生バイト。</summary>
        public byte[] ObjectVersionData { get; set; }

        /// <summary>FWorldPartitionValidationData（検証グリッド）の生バイト。</summary>
        public byte[] ValidationGrids { get; set; }

        /// <summary>サブレベル群（mPerLevelDataMap）。</summary>
        public List<PartitionLevel> SubLevels { get; set; } = new List<PartitionLevel>();

        /// <summary>
        ///     永続＆ランタイムレベルのオブジェクト群（編集対象の工場オブジェクトはここに集中）。
        ///     ヘッダーは展開済み、内部データは <see cref="SaveObject.RawData"/> に不透明保持する（第2段階）。
        /// </summary>
        public List<SaveObject> PersistentObjects { get; set; } = new List<SaveObject>();

        /// <summary>
        ///     永続 TOC blob のオブジェクトヘッダー列の後ろに続く末尾構造（LevelToDestroyedActorsMap）の生バイト。
        /// </summary>
        public byte[] PersistentTocTrailing { get; set; }

        /// <summary>LevelToDestroyedActorsMap（FString キー → 参照配列）。</summary>
        public List<KeyValuePair<string, List<ObjectReference>>> LevelToDestroyedActorsMap { get; set; }
            = new List<KeyValuePair<string, List<ObjectReference>>>();

        /// <summary>FUnresolvedWorldSaveData（未解決の破棄アクター）。</summary>
        public List<ObjectReference> UnresolvedDestroyedActors { get; set; } = new List<ObjectReference>();

        public static SaveBodyV2 Parse(BinaryReader reader, FSaveHeader header)
        {
            var body = new SaveBodyV2();

            reader.ReadInt64(); // bodyLength（残バイト長。再シリアライズ時に再計算するので保持不要）

            if (header.SaveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                body.ObjectVersionData = CaptureRaw(reader, SkipObjectVersionData);

            body.ValidationGrids = CaptureRaw(reader, SkipValidationGrids);

            // mPerLevelDataMap
            var subLevelCount = reader.ReadInt32();
            for (var i = 0; i < subLevelCount; i++)
            {
                var level = new PartitionLevel
                {
                    Name = reader.ReadLengthPrefixedString(),
                    TocBlob = ReadBlob64(reader),
                    DataBlob = ReadBlob64(reader)
                };

                if (header.SaveVersion >= FSaveCustomVersion.AddedPerStreamingLevelSaveVersion)
                    level.SaveVersion = reader.ReadInt32();

                level.DestroyedActors = ReadReferences(reader);

                if (header.SaveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                {
                    var hasVersionData = reader.ReadInt32();
                    if (hasVersionData == 1)
                        level.OptionalVersionData = CaptureRaw(reader, SkipObjectVersionData);
                }

                body.SubLevels.Add(level);
            }

            // FPersistentAndRuntimeSaveData（編集対象なのでオブジェクト単位に展開）
            var persistentToc = ReadBlob64(reader);
            var persistentData = ReadBlob64(reader);
            body.PersistentObjects = ParseLevelObjects(persistentToc, persistentData, header.SaveVersion, out var trailing);
            body.PersistentTocTrailing = trailing;

            var mapSize = reader.ReadInt32();
            for (var i = 0; i < mapSize; i++)
            {
                var key = reader.ReadLengthPrefixedString();
                var refs = ReadReferences(reader);
                body.LevelToDestroyedActorsMap.Add(new KeyValuePair<string, List<ObjectReference>>(key, refs));
            }

            // FUnresolvedWorldSaveData
            body.UnresolvedDestroyedActors = ReadReferences(reader);

            return body;
        }

        public void Serialize(BinaryWriter writer, FSaveHeader header, List<SaveObject> persistentObjects)
        {
            // bodyLength（int64）プレースホルダ。本体書き込み後に実長で埋め直す。
            var lengthPos = writer.BaseStream.Position;
            writer.Write(0L);
            var contentStart = writer.BaseStream.Position;

            if (header.SaveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                writer.Write(ObjectVersionData);

            writer.Write(ValidationGrids);

            writer.Write(SubLevels.Count);
            foreach (var level in SubLevels)
            {
                writer.WriteLengthPrefixedString(level.Name);
                WriteBlob64(writer, level.TocBlob);
                WriteBlob64(writer, level.DataBlob);

                if (header.SaveVersion >= FSaveCustomVersion.AddedPerStreamingLevelSaveVersion)
                    writer.Write(level.SaveVersion);

                WriteReferences(writer, level.DestroyedActors);

                if (header.SaveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                {
                    if (level.OptionalVersionData != null)
                    {
                        writer.Write(1);
                        writer.Write(level.OptionalVersionData);
                    }
                    else
                    {
                        writer.Write(0);
                    }
                }
            }

            WriteBlob64(writer, BuildToc(persistentObjects, PersistentTocTrailing, header.SaveVersion));
            WriteBlob64(writer, BuildData(persistentObjects, header.SaveVersion));

            writer.Write(LevelToDestroyedActorsMap.Count);
            foreach (var entry in LevelToDestroyedActorsMap)
            {
                writer.WriteLengthPrefixedString(entry.Key);
                WriteReferences(writer, entry.Value);
            }

            WriteReferences(writer, UnresolvedDestroyedActors);

            // bodyLength を埋め直す（int64 フィールド直後からの総バイト数）
            var endPos = writer.BaseStream.Position;
            writer.BaseStream.Position = lengthPos;
            writer.Write(endPos - contentStart);
            writer.BaseStream.Position = endPos;
        }

        /// <summary>
        ///     レベルの TOC blob（オブジェクトヘッダー列＋末尾構造）と DATA blob（per-object フレーム＋内部データ）を
        ///     解析して SaveObject 列を作る。内部データはプロパティ未パースのまま <see cref="SaveObject.RawData"/> に保持する。
        /// </summary>
        private static List<SaveObject> ParseLevelObjects(byte[] tocBlob, byte[] dataBlob, FSaveCustomVersion saveVersion, out byte[] tocTrailing)
        {
            var objects = new List<SaveObject>();

            using (var tocStream = new MemoryStream(tocBlob))
            using (var tocReader = new BinaryReader(tocStream))
            {
                var count = tocReader.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    var type = tocReader.ReadInt32();
                    switch (type)
                    {
                        case SaveEntity.TypeID:
                            objects.Add(new SaveEntity(tocReader, saveVersion));
                            break;
                        case SaveComponent.TypeID:
                            objects.Add(new SaveComponent(tocReader, saveVersion));
                            break;
                        default:
                            throw new System.InvalidOperationException($"Unexpected TOC object type {type} @ {tocStream.Position - 4}");
                    }
                }

                tocTrailing = tocReader.ReadBytes((int)(tocStream.Length - tocStream.Position));
            }

            using (var dataStream = new MemoryStream(dataBlob))
            using (var dataReader = new BinaryReader(dataStream))
            {
                var count = dataReader.ReadInt32();
                if (count != objects.Count)
                    throw new InvalidDataException($"DATA オブジェクト数 {count} が TOC オブジェクト数 {objects.Count} と一致しません");
                for (var i = 0; i < count; i++)
                {
                    var obj = objects[i];
                    obj.DataSaveVersion = dataReader.ReadInt32();
                    obj.ShouldMigrate = dataReader.ReadInt32() != 0;
                    var len = dataReader.ReadInt32();
                    // 破損ファイルでの負値/過大長による例外・サイレント短読み（フレーム desync）を防ぐ。
                    // 正当な save では len は厳密に一致するのでこの分岐は発火しない。
                    if (len < 0 || dataStream.Position + len > dataStream.Length)
                        throw new InvalidDataException($"オブジェクトデータ長 {len} がストリーム範囲外です");
                    obj.RawData = dataReader.ReadBytes(len);

                    // ハイブリッド読み取り: 内部データをプロパティ列として解釈してみる。失敗したらそのオブジェクトは
                    // 内部不透明（RawData のまま）にフォールバックする。保存時は RawData が優先されるので
                    // パース可否に関わらず round-trip は保証される。
                    TryParseProperties(obj, len);

                    if (saveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                    {
                        var hasVersionData = dataReader.ReadInt32();
                        if (hasVersionData == 1)
                            obj.OptionalVersionData = CaptureRaw(dataReader, SkipObjectVersionData);
                    }
                }
            }

            return objects;
        }

        /// <summary>
        ///     1.0+ オブジェクトの内部 data bytes（プロパティ列）を新タグ書式で解釈し、
        ///     <see cref="SaveObject.DataFields"/> を構築する。失敗時は何もしない（RawData フォールバック）。
        ///     Phase 3a 進行中: 現状は安全停止のみ。Phase 3b で実装本体が入る。
        /// </summary>
        private static void TryParseProperties(SaveObject obj, int dataLen)
        {
            // Phase 3a 完了までは常にフォールバック（RawData 保持）。
            // 後続フェーズでここに PropertiesListV2.Parse を入れ、成功時のみ obj.DataFields を設定する。
        }

        /// <summary>SaveObject 列から TOC blob を再構築する。</summary>
        private static byte[] BuildToc(List<SaveObject> objects, byte[] tocTrailing, FSaveCustomVersion saveVersion)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(objects.Count);
                foreach (var obj in objects)
                {
                    writer.Write(obj is SaveEntity ? SaveEntity.TypeID : SaveComponent.TypeID);
                    obj.SerializeNewHeader(writer, saveVersion);
                }

                if (tocTrailing != null)
                    writer.Write(tocTrailing);

                return ms.ToArray();
            }
        }

        /// <summary>SaveObject 列から DATA blob を再構築する。</summary>
        private static byte[] BuildData(List<SaveObject> objects, FSaveCustomVersion saveVersion)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(objects.Count);
                foreach (var obj in objects)
                {
                    writer.Write(obj.DataSaveVersion);
                    writer.Write(obj.ShouldMigrate ? 1 : 0);

                    // 1.0 オブジェクトは内部データを RawData に逐語保持して round-trip させる設計。
                    // ディスクから読んだオブジェクトは ReadBytes により必ず非 null（長さ0でも byte[0]）なので
                    // この throw は正当なセーブの再書き込みでは決して発火しない。RawData が null になるのは
                    // チート/複製などプログラムから DataFields だけ詰めて生成された未対応オブジェクトのみで、
                    // それを ?? new byte[0] で空フレームとして黙って書くと壊れたオブジェクトを生む。
                    if (obj.RawData == null)
                        throw new System.NotSupportedException(
                            $"1.0+ オブジェクト '{obj.InstanceName}' は RawData を持ちません。" +
                            "新規 1.0 オブジェクトの追加（DataFields からのプロパティ書き出し）は未対応です。");
                    var raw = obj.RawData;
                    writer.Write(raw.Length);
                    writer.Write(raw);

                    if (saveVersion >= FSaveCustomVersion.AddedSaveObjectVersionData)
                    {
                        if (obj.OptionalVersionData != null)
                        {
                            writer.Write(1);
                            writer.Write(obj.OptionalVersionData);
                        }
                        else
                        {
                            writer.Write(0);
                        }
                    }
                }

                return ms.ToArray();
            }
        }

        private static byte[] ReadBlob64(BinaryReader reader)
        {
            var size = reader.ReadInt64();
            // 破損ファイルでの過大/負値による OOM・例外を防ぐ。残りストリーム長で検証（正当な大ブロブは弾かない）。
            if (size < 0 || reader.BaseStream.Position + size > reader.BaseStream.Length)
                throw new InvalidDataException($"ブロブ長 {size} がストリーム範囲外です");
            return reader.ReadBytes((int)size);
        }

        private static void WriteBlob64(BinaryWriter writer, byte[] blob)
        {
            writer.Write((long)blob.Length);
            writer.Write(blob);
        }

        private static List<ObjectReference> ReadReferences(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            // 各 ObjectReference は最低 8 byte（長さ0の FString 2 本）。残りストリーム長で上限検証し、
            // 破損ファイルでの過大 capacity による OOM を防ぐ。固定上限ではなくファイル長基準なので正当な大規模レベルは弾かない。
            var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (count < 0 || (long)count * 8 > remaining)
                throw new InvalidDataException($"参照数 {count} が不正です（ストリーム残り {remaining} byte）");
            var list = new List<ObjectReference>(count);
            for (var i = 0; i < count; i++)
                list.Add(new ObjectReference(reader));
            return list;
        }

        private static void WriteReferences(BinaryWriter writer, List<ObjectReference> refs)
        {
            writer.Write(refs.Count);
            foreach (var r in refs)
            {
                writer.WriteLengthPrefixedString(r.LevelName);
                writer.WriteLengthPrefixedString(r.PathName);
            }
        }

        /// <summary>start から skip を実行し、消費した範囲の生バイトを返す（位置は skip 後のまま）。</summary>
        private static byte[] CaptureRaw(BinaryReader reader, System.Action<BinaryReader> skip)
        {
            var start = reader.BaseStream.Position;
            skip(reader);
            var end = reader.BaseStream.Position;
            reader.BaseStream.Position = start;
            var bytes = reader.ReadBytes((int)(end - start));
            return bytes;
        }

        private static void SkipObjectVersionData(BinaryReader reader)
        {
            reader.ReadInt32();                 // 先頭の 0
            reader.ReadInt32();                 // UE4ObjectVersion
            reader.ReadInt32();                 // UE5ObjectVersion
            reader.ReadInt32();                 // customVersionFormat
            reader.ReadUInt16();                // engineMajor
            reader.ReadUInt16();                // engineMinor
            reader.ReadUInt16();                // enginePatch
            reader.ReadUInt32();                // changelist
            reader.ReadLengthPrefixedString();  // branch
            var cvCount = reader.ReadInt32();
            // (GUID 16 + int32) * count。破損ファイルでの不正シーク（負値/終端超過）を防ぐ。long 算術で桁あふれも回避。
            if (cvCount < 0 || reader.BaseStream.Position + (long)cvCount * 20 > reader.BaseStream.Length)
                throw new InvalidDataException($"カスタムバージョン数 {cvCount} が不正です");
            reader.BaseStream.Position += (long)cvCount * 20;
        }

        private static void SkipValidationGrids(BinaryReader reader)
        {
            var numGrids = reader.ReadInt32();
            for (var g = 0; g < numGrids; g++)
            {
                reader.ReadLengthPrefixedString(); // gridName
                reader.ReadInt32();                // cellSize
                reader.ReadUInt32();               // gridHash
                var cellCount = reader.ReadInt32();
                for (var c = 0; c < cellCount; c++)
                {
                    reader.ReadLengthPrefixedString(); // cellName
                    reader.ReadUInt32();               // cellHash
                }
            }
        }
    }
}
