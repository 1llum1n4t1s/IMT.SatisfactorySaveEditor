namespace SatisfactorySaveParser.Save
{
    public enum SaveHeaderVersion
    {
        // First version
        InitialVersion = 0,

        // @2017-01-20: Added BuildVersion, MapName and MapOptions
        PrepareForLoadingMaps,

        // @2017-02-07: Added SessionId for autosaves
        AddedSessionId,

        // @2018-02-23 Added PlayDuration to header
        AddedPlayDuration,

        // @2018-04-10 SessionID from int32 to FString, also added when the save was saved
        SessionIDStringAndSaveTimeAdded,

        // @2019-01-15 Added session visibility to the header so we can set it up with the same visibility
        AddedSessionVisibility,

        // @2019-06-19 This was put in the wrong save version thingy and is now on experimental so cant remnove it.
        LookAtTheComment,

        // @2021-01-22 UE4.25 Engine Upgrade. FEditorObjectVersion Changes occurred (notably with FText serialization)
        UE425EngineUpdate,

        // @2021-03-24 Added Modding properties and support
        AddedModdingParams,

        // @2021-04-15 UE4.26 Engine Upgrade. FEditorObjectVersion Changes occurred
        UE426EngineUpdate,

        // @2022 UE4.27 Engine Upgrade（Update 6 系）
        UE427EngineUpdate,

        // @2023 セーブ名をヘッダーへ追加（Update 8 系で出現）
        AddedSaveName,

        // 予約（中間バージョン）
        ReservedHeader12,

        // @2024-09 Satisfactory 1.0。ワールドパーティション対応に伴い
        // SaveIdentifier / IsPartitionedWorld / FMD5Hash(SaveDataHash) を追加
        AddedWorldPartitionAndHash,

        // @2024+ 1.1/1.2。クリエイティブモードフラグを追加
        AddedCreativeMode,

        // -----<new versions can be added above this line>-----
        VersionPlusOne,
        LatestVersion = VersionPlusOne - 1 // Last version to use
    }
}
