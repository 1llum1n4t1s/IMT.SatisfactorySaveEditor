
using SatisfactorySaveEditor.Model;

using SatisfactorySaveParser;

namespace SatisfactorySaveEditor.Cheats
{
    public class RestoreSlugsCheat : ICheat
    {
        public string NameKey => "CheatRestoreSlugs";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            saveGame.CollectedObjects.RemoveAll(x => x.PathName.Contains("PersistentLevel.BP_Crystal"));
            return true;
        }
    }
}
