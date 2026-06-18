using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

using System.Windows;

namespace SatisfactorySaveEditor.Cheats
{
    public class NoCostCheat : ICheat
    {
        public string NameKey => "CheatNoCost";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var gameState = rootItem.FindChild("Persistent_Level:PersistentLevel.BP_GameState_C_*", false);
            if (gameState == null)
            {
                MessageBox.Show(Resources.MsgNoGameState_Body, Resources.MsgNoGameState_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var numAdditionalSlots = gameState.FindOrCreateField<BoolPropertyViewModel>("mCheatNoCost");
            numAdditionalSlots.Value = !numAdditionalSlots.Value;
            MessageBox.Show(numAdditionalSlots.Value ? Resources.MsgNoCostEnabled_Body : Resources.MsgNoCostDisabled_Body, Resources.MsgSuccess_Title, MessageBoxButton.OK, MessageBoxImage.Information);

            return true;
        }
    }
}
