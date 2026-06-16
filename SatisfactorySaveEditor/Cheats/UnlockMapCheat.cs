using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

using System.Windows;

namespace SatisfactorySaveEditor.Cheats
{
    public class UnlockMapCheat : ICheat
    {
        public string Name => "Unlock map";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var gameState = rootItem.FindChild("Persistent_Level:PersistentLevel.UnlockSubsystem", false);
            if (gameState == null)
            {
                MessageBox.Show(Resources.MsgNoUnlockSubsystemMap_Body, "Cannot find UnlockSubsystem", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var isMapUnlocked = gameState.FindOrCreateField<BoolPropertyViewModel>("mIsMapUnlocked");
            isMapUnlocked.Value = true;

            MessageBox.Show(Resources.MsgMapUnlocked_Body, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}