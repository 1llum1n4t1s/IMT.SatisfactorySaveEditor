using SuperLightLogger;
using SatisfactorySaveParser.Exceptions;
using System.IO;

namespace SatisfactorySaveParser.Save
{
    /// <summary>
    ///     Engine class: FSaveHeader
    ///     Header: FGSaveSystem.h
    /// </summary>
    public class FSaveHeader
    {
        private static readonly ILog log = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Save version number
        /// </summary>
        public SaveHeaderVersion HeaderVersion { get; set; }
        /// <summary>
        ///     Save build (feature) number
        /// </summary>
        public FSaveCustomVersion SaveVersion { get; set; }

        /// <summary>
        ///     Unknown magic int
        ///     Seems to always be 66297
        /// </summary>
        public int BuildVersion { get; set; }

        /// <summary>
        ///     セーブ名。1.0(HeaderVersion>=AddedSaveName) で追加。BuildVersion と MapName の間に格納される。
        /// </summary>
        public string SaveName { get; set; }

        /// <summary>
        ///     The name of what appears to be the root object of the save.
        ///     Seems to always be "Persistent_Level"
        /// </summary>
        public string MapName { get; set; }
        /// <summary>
        ///     An URL style list of arguments of the session.
        ///     Contains the startloc, sessionName and Visibility
        /// </summary>
        public string MapOptions { get; set; }
        /// <summary>
        ///     Name of the saved game as entered when creating a new game
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        ///     Amount of seconds spent in this save
        /// </summary>
        public int PlayDuration { get; set; }
        /// <summary>
        ///     Unix timestamp of when the save was saved
        /// </summary>
        public long SaveDateTime { get; set; }

        public ESessionVisibility SessionVisibility { get; set; }

        /// <summary>
        ///     The FEditorObjectVersion that this save file was written with
        /// </summary>
        public int EditorObjectVersion { get; set; }

        /// <summary>
        ///     Generic MetaData - Requested by Mods
        /// </summary>
        public string ModMetadata { get; set; }

        /// <summary>
        ///     Was this save ever saved with mods enabled?
        /// </summary>
        public bool IsModdedSave { get; set; }

        /// <summary>
        ///     セーブ固有の識別子。1.0(HeaderVersion>=AddedWorldPartitionAndHash) で追加。
        /// </summary>
        public string SaveIdentifier { get; set; }

        /// <summary>
        ///     ワールドパーティション化されたセーブか。1.0 で追加。
        /// </summary>
        public bool IsPartitionedWorld { get; set; }

        /// <summary>
        ///     FMD5Hash の bIsValid。true のとき <see cref="SaveDataHash"/> に 16byte が入る。1.0 で追加。
        /// </summary>
        public bool HasSaveDataHash { get; set; }

        /// <summary>
        ///     セーブデータの MD5 ハッシュ（16byte）。1.0 で追加。
        /// </summary>
        public byte[] SaveDataHash { get; set; }

        /// <summary>
        ///     クリエイティブモード有効フラグ。1.1/1.2(HeaderVersion>=AddedCreativeMode) で追加。
        /// </summary>
        public bool IsCreativeModeEnabled { get; set; }

        /// <summary>
        ///     1.0 以降の新ボディ・新圧縮チャンク形式か（HeaderVersion>=AddedWorldPartitionAndHash）。
        /// </summary>
        public bool IsNewFormat => HeaderVersion >= SaveHeaderVersion.AddedWorldPartitionAndHash;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((int)HeaderVersion);
            writer.Write((int)SaveVersion);
            writer.Write(BuildVersion);

            if (HeaderVersion >= SaveHeaderVersion.AddedSaveName)
                writer.WriteLengthPrefixedString(SaveName);

            writer.WriteLengthPrefixedString(MapName);
            writer.WriteLengthPrefixedString(MapOptions);
            writer.WriteLengthPrefixedString(SessionName);

            writer.Write(PlayDuration);
            writer.Write(SaveDateTime);

            if (HeaderVersion >= SaveHeaderVersion.AddedSessionVisibility)
                writer.Write((byte)SessionVisibility);

            if (HeaderVersion >= SaveHeaderVersion.UE425EngineUpdate)
                writer.Write(EditorObjectVersion);

            if (HeaderVersion >= SaveHeaderVersion.AddedModdingParams)
            {
                writer.WriteLengthPrefixedString(ModMetadata);
                writer.Write(IsModdedSave ? 1 : 0);
            }

            if (HeaderVersion >= SaveHeaderVersion.AddedWorldPartitionAndHash)
            {
                writer.WriteLengthPrefixedString(SaveIdentifier);
                writer.Write(IsPartitionedWorld ? 1 : 0);

                writer.Write(HasSaveDataHash ? 1 : 0);
                if (HasSaveDataHash)
                {
                    if (SaveDataHash == null || SaveDataHash.Length != 16)
                        throw new InvalidDataException($"HasSaveDataHash=true だが SaveDataHash が 16byte ではありません（Length={(SaveDataHash == null ? "null" : SaveDataHash.Length.ToString())}）");
                    writer.Write(SaveDataHash);
                }
            }

            if (HeaderVersion >= SaveHeaderVersion.AddedCreativeMode)
                writer.Write(IsCreativeModeEnabled ? 1 : 0);
        }

        public static FSaveHeader Parse(BinaryReader reader)
        {
            var header = new FSaveHeader
            {
                HeaderVersion = (SaveHeaderVersion)reader.ReadInt32(),
                SaveVersion = (FSaveCustomVersion)reader.ReadInt32(),
                BuildVersion = reader.ReadInt32()
            };

            if (header.HeaderVersion >= SaveHeaderVersion.AddedSaveName)
                header.SaveName = reader.ReadLengthPrefixedString();

            header.MapName = reader.ReadLengthPrefixedString();
            header.MapOptions = reader.ReadLengthPrefixedString();
            header.SessionName = reader.ReadLengthPrefixedString();

            header.PlayDuration = reader.ReadInt32();
            header.SaveDateTime = reader.ReadInt64();

            log.Debug($"Read save header: HeaderVersion={header.HeaderVersion}, SaveVersion={(int)header.SaveVersion}, BuildVersion={header.BuildVersion}, SaveName={header.SaveName}, MapName={header.MapName}, MapOpts={header.MapOptions}, Session={header.SessionName}, PlayTime={header.PlayDuration}, SaveTime={header.SaveDateTime}");

            if (header.HeaderVersion > SaveHeaderVersion.LatestVersion)
                throw new UnknownSaveVersionException(header.HeaderVersion);

            if (header.SaveVersion < FSaveCustomVersion.DROPPED_WireSpanFromConnnectionComponents || header.SaveVersion > FSaveCustomVersion.LatestVersion)
                throw new UnknownBuildVersionException(header.SaveVersion);

            // LatestVersion を 1.0 用に 60 へ引き上げたことで、partitioned でない（IsNewFormat==false）中間形式
            // （Update 6/7/8）の SaveVersion も上の範囲を通過してしまう。それらは旧 LoadData/SaveData のレイアウト
            // では正しく読めず破損するため、新形式でなければ legacy 上限（TrainBlueprintClassAdded＝U5）超えを弾く。
            if (!header.IsNewFormat && header.SaveVersion > FSaveCustomVersion.TrainBlueprintClassAdded)
                throw new UnknownBuildVersionException(header.SaveVersion);

            if (header.HeaderVersion >= SaveHeaderVersion.AddedSessionVisibility)
            {
                header.SessionVisibility = (ESessionVisibility)reader.ReadByte();
                log.Debug($"SessionVisibility={header.SessionVisibility}");
            }

            if (header.HeaderVersion >= SaveHeaderVersion.UE425EngineUpdate)
            {
                header.EditorObjectVersion = reader.ReadInt32();
                log.Debug($"EditorObjectVersion={header.EditorObjectVersion}");
            }

            if (header.HeaderVersion >= SaveHeaderVersion.AddedModdingParams)
            {
                header.ModMetadata = reader.ReadLengthPrefixedString();
                header.IsModdedSave = reader.ReadInt32() > 0;
                log.Debug($"ModMetadata={header.ModMetadata}, IsModdedSave={header.IsModdedSave}");
            }

            if (header.HeaderVersion >= SaveHeaderVersion.AddedWorldPartitionAndHash)
            {
                header.SaveIdentifier = reader.ReadLengthPrefixedString();
                header.IsPartitionedWorld = reader.ReadInt32() > 0;

                header.HasSaveDataHash = reader.ReadInt32() > 0;
                if (header.HasSaveDataHash)
                    header.SaveDataHash = reader.ReadBytes(16);

                log.Debug($"SaveIdentifier={header.SaveIdentifier}, IsPartitionedWorld={header.IsPartitionedWorld}, HasSaveDataHash={header.HasSaveDataHash}");
            }

            if (header.HeaderVersion >= SaveHeaderVersion.AddedCreativeMode)
            {
                header.IsCreativeModeEnabled = reader.ReadInt32() > 0;
                log.Debug($"IsCreativeModeEnabled={header.IsCreativeModeEnabled}");
            }

            return header;
        }
    }
}
