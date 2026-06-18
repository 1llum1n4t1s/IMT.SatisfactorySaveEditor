using System.Collections.Generic;
using System.Linq;
using System.Windows;

using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

namespace SatisfactorySaveEditor.Cheats
{
    public class DeduplicateSchematicsCheat : ICheat
    {
        public string NameKey => "CheatDeduplicateSchematics";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var schematicManager = rootItem.FindChild("Persistent_Level:PersistentLevel.schematicManager", false);
            if (schematicManager == null)
            {
                MissingTagMsg("schematicManager");
                return false;
            }

            var available = schematicManager.FindField<ArrayPropertyViewModel>("mAvailableSchematics");
            var found = new HashSet<string>();
            var removed = 0;
            foreach (var obj in available.Elements.Cast<ObjectPropertyViewModel>().ToList())
            {
                if(found.Contains(obj.Str2))
                {
                    available.RemoveElementCommand.Execute(obj);
                    removed++;
                }
                else
                {
                    found.Add(obj.Str2);
                }
            }

            MessageBox.Show(string.Format(Resources.MsgDuplicateSchematicsRemoved_Body, removed), Resources.MsgSuccess_Title, MessageBoxButton.OK, MessageBoxImage.Information);

            return true;
        }

        private void MissingTagMsg(string tagName)
        {
            MessageBox.Show(string.Format(Resources.MsgSaveMissingSubsystem_Body, tagName), string.Format(Resources.MsgCannotFindSubsystem_Title, tagName), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
