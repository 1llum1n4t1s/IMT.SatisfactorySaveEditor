namespace SatisfactorySaveParser.PropertyTypes.V2
{
    /// <summary>
    ///     1.0+ プロパティ。タグと本体バイトを保持する。Phase 3a 段階では本体は不透明バイトのまま。
    ///     Phase 3b で型に応じた値展開を行い、Phase 3c で書き戻しを有効化する予定。
    /// </summary>
    public class PropertyV2
    {
        public FPropertyTagV2 Tag { get; set; }

        /// <summary>本体生バイト（タグの BinarySize 分）。BoolProperty はタグ flags に値があるため 0 バイト。</summary>
        public byte[] Body { get; set; }
    }
}
