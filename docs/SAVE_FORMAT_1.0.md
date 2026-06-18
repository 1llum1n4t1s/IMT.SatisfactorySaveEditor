# Satisfactory 1.0+ セーブ形式 (HeaderVersion 13/14, SaveVersion 51〜60)

実セーブ（`くれぱす` / ゲーム build 1.2.0, BuildVersion 493833, HeaderVersion 14, SaveVersion 60）を
リバースエンジニアリングし、**ボディ全体をバイト完全 round-trip 検証して確定**した仕様。
旧 Update 5 形式（HeaderVersion 9 / SaveVersion 27 まで）とは別構造。

すべてリトルエンディアン。`FString` = `int32 len` + bytes。len>0 は UTF-8 (末尾 null 含む)、len<0 は UTF-16(`-len` 文字、末尾 null 含む)、len==0 は空。

## 1. ヘッダー（非圧縮）

```
int32  HeaderVersion              (14)
int32  SaveVersion                (60)
int32  BuildVersion               (493833)
FString SaveName                  ← 新規。例 "くれぱす_160626-193833"
FString MapName                   ("Persistent_Level")
FString MapOptions                ("?ClientIdentity=...")
FString SessionName               ("くれぱす")
int32  PlayDurationSeconds
int64  SaveDateTime               (.NET ticks)
byte   SessionVisibility
int32  EditorObjectVersion        (HeaderVersion>=7)
FString ModMetadata               (HeaderVersion>=8)
int32  IsModdedSave               (HeaderVersion>=8)
FString SaveIdentifier            ← 新規。例 "tc17-k-8DSm6mKu-_qIftw"
int32  IsPartitionedWorld         ← 新規 (bool)
[16]   SaveDataHash.bIsValid=int32 + 16byte MD5  ← 新規 (FMD5Hash: int32 bIsValid, true なら 16byte)
int32  IsCreativeModeEnabled      ← 新規 (bool)
<圧縮チャンク開始>
```

## 2. 圧縮チャンク（1.0 形式）

```
per chunk:
  int32  magic   = 0x9E2A83C1
  int32  marker  = 0x22222222     ← 新規(旧形式は上位32bit=0)
  int64  maxChunkSize = 131072
  byte   compressorAlgo = 3 (zlib) ← 新規
  int64  compressedSize
  int64  uncompressedSize
  int64  compressedSize  (繰り返し)
  int64  uncompressedSize(繰り返し)
  [compressedSize バイトの zlib データ]
```
展開後ボディの先頭は **int64** の bodyLength（旧形式は int32）。

## 3. ボディ（展開後）

```
int64  bodyLength
FSaveObjectVersionData            (SaveVersion>=53)
FWorldPartitionValidationData
TMap<FString,FPerStreamingLevelSaveData> mPerLevelDataMap
FPersistentAndRuntimeSaveData
FUnresolvedWorldSaveData
```

### FSaveObjectVersionData
```
int32  0
int32  UE4ObjectVersion (522)
int32  UE5ObjectVersion (1017)
int32  customVersionFormat (3)
uint16 engineMajor, uint16 engineMinor, uint16 enginePatch
uint32 changelist
FString branch  ("++FactoryGame+rel-main-1.2.0")
int32  customVersionCount (13)
{ [16]GUID + int32 version } × count
```

### FWorldPartitionValidationData
```
int32 numGrids (7)
per grid:
  FString gridName  ("MainGrid" 等)
  int32   cellSize
  uint32  gridHash
  int32   cellCount
  per cell: FString cellName + uint32 cellHash
```

### mPerLevelDataMap
```
int32 numSubLevels (3366 = パーティション化ワールドのセル毎)
per sublevel:
  FString levelName
  int64   TOCsize, [TOC blob]
  int64   DATAsize, [DATA blob]
  int32   SaveVersion                 (SaveVersion>=51)
  int32   destroyedActorsCount + refs  (FObjectReferenceDisc)
  int32   hasVersionData (TOptional, SaveVersion>=53); ==1 なら FSaveObjectVersionData
```

### FPersistentAndRuntimeSaveData
```
int64 TOCsize, [TOC blob]
int64 DATAsize, [DATA blob]
TMap<FString,TArray<FObjectReferenceDisc>> LevelToDestroyedActorsMap
   = int32 size + { FString levelKey + (int32 count + refs) }
```

### FUnresolvedWorldSaveData
```
int32 destroyedActorsCount + refs
```

`FObjectReferenceDisc` = `FString levelName + FString pathName`。

## 4. TOC blob 内部
```
int32 numObjects
per object:
  int32 isActor   (1=Actor / 0=Object。既存 SaveEntity.TypeID=1 / SaveComponent.TypeID=0 と一致)
  if actor: FActorSaveHeader
  else:     FObjectSaveHeader
末尾: DestroyedActors(TArray) または(persistent) LevelToDestroyedActorsMap(TMap)
      ※ blob は長さ前置きなので、numObjects 読了後の残バイトを保持して round-trip
```

### FActorSaveHeader
```
FString ClassName
FObjectReferenceDisc Reference (levelName, pathName)
uint32  ObjectFlags             (SaveVersion>=49。例 0x280008)
int32   NeedTransform (bool)
float[4] Rotation (quat)
float[3] Translation (cm)
float[3] Scale3D
int32   WasPlacedInLevel (bool)
```

### FObjectSaveHeader
```
FString ClassName
FObjectReferenceDisc Reference
uint32  ObjectFlags             (SaveVersion>=49)
FString OuterPathName
```

## 5. DATA blob 内部
```
int32 numObjects   (TOC と同数・同順)
per object:
  int32 SaveVersion
  int32 ShouldMigrateObjectRefsToPersistent (bool)
  int32 dataLen, [data bytes]   ← data は既存 SerializedFields(プロパティ列 + "None" + int32 0 + trailing)
  int32 hasPerObjectVersionData (SaveVersion>=53); ==1 なら FSaveObjectVersionData
```

## 6. プロパティ（オブジェクト内部 data bytes）の新タグ書式

ボディの DATA blob 内の `[data bytes]`（既存 SerializedFields 相当）は、1.0+ で **PropertyTag の書式が完全に別物**に変わっている。
ゲート: `SaveVersion >= SerializeDataPackageVersionAndCustomVersions`（Satisfactory 側のフラグ）
**AND** `UE5ObjectVersion >= 1012`（`PROPERTY_TAG_COMPLETE_TYPE_NAME`）。両方を満たすときは新タグ。

```
FPropertyTagNode = {
  FString name
  int32   childCount
  FPropertyTagNode children[childCount]
}
```

```
PropertyTag (new) = {
  FString name           // "None" なら以降を読まずプロパティリスト終端
  FPropertyTagNode tagType   // ← 型名・サブ型・StructName・Map 値型などを再帰に圧縮
  int32  binarySize       // 続くプロパティ本体のバイト数
  uint8  flags
    0x01: int32 index が続く
    0x02: GUID propertyGuid（16 byte）が続く
    0x10: BoolProperty の値（タグだけで値を保持、本体バイトは 0）
    0x04, 0x08 ほかは未確定（実バイトで判別）
  [int32 index]            if flags & 0x01
  [GUID propertyGuid]      if flags & 0x02
  [binarySize バイトのプロパティ本体]
}
```

旧書式（U5まで）の `Type FString` 直書きや BoolProperty 直値は**消滅**。
- BoolProperty: 値は `flags & 0x10`。本体バイトは 0
- ArrayProperty: 旧 `innerType FString` は tagType.children[0].name へ
- StructProperty: 旧 `structName FString + GUID + padding` は tagType.children[0].name + GUID
- MapProperty: 旧 `keyType + valueType` は tagType.children[0].name + tagType.children[1].name
- ArrayProperty<Struct> の **per-element wrapper（旧形式は要素毎に struct タグ）も消滅**。要素は親タグの structName 1 回だけで並ぶ

プロパティリスト終端: `name == "None"` のタグ単体（タグ本体は読まない）。

## 7. オブジェクト内部データの先頭フレーミング（DATA blob の per-object data bytes 先頭）

実測（C# RawData 直接ダンプ）で次の差が判明:

**SaveEntity (Actor) の data 構造**（旧 U5 と同じ枠が維持）:
```
FString parentRoot           // 空なら4byte
FString parentName           // 空なら4byte
int32   componentCount
{ FString levelName, FString pathName } × componentCount
[?  parent を持つ Actor だけ 2 byte の追加プレフィクスがある模様（要追加リバース）]
PropertyTag... "None"
trailing bytes
```

**SaveComponent の data 構造**:
```
byte    unknownFlag          // 0x00 を実測。意味は未確定
PropertyTag... "None"
trailing bytes
```

つまり旧 SaveEntity.ParseData 相当を 1.0 でも使えるが、**Component には 1 byte 追加プレフィクス**、**Actor にも parent/components の後に 1 byte 追加プレフィクス**、**parent 持ち Actor にはさらに 2 byte の追加プレフィクス**がある。これらをスキップしないとプロパティリストに到達できない。

### Phase 3a 実測（C# 実装ベース）

`PropertiesListV2.Parse` を `RawData[skip..]` に対して呼んだ際の完走率（各オブジェクトのプロパティリストを "None" 終端まで読み切れた件数）:

| 種別 | サンプル | 成功 | 失敗 | 備考 |
|---|---|---|---|---|
| Actor (parent なし) | 23 | 23 | 0 | skip = 12 + 1 byte で 100% |
| Actor (parent あり) | 2 | 0 | 2 | 追加 2 byte の取り扱いが未確定 |
| Component | 25 | 25 | 0 | skip = 1 byte で 100% |

→ 1.0+ の新タグ書式（FPropertyTagNode + flags ベース）の C# 実装が実バイトで成立することを確認。残課題は parent 持ち Actor の prefix 仕様。

## 実装方針（C#）
- ヘッダー/圧縮/コンテナ枠を 1.0 対応へ拡張。blob は生バイト保持で round-trip を保証。
- persistent level のオブジェクトを既存 SaveEntity/SaveComponent/SerializedFields で展開して編集 UI に出す。
  sublevel は当面 blob 不透明保持（round-trip 保証、編集対象は persistent に集中）。
- 旧 Update 5 形式（SaveVersion < SaveFileIsCompressed/<新圧縮）は既存コードパスを維持。
