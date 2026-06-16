using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.View;
using SatisfactorySaveEditor.ViewModel;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

using System.Windows;

namespace SatisfactorySaveEditor.Cheats
{
    public class ArmSlotsCheat : ICheat
    {
        public string Name => "Set arm slot count...";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var gameState = rootItem.FindChild("Persistent_Level:PersistentLevel.UnlockSubsystem", false);
            if (gameState == null)
            {
                MessageBox.Show(Resources.MsgNoUnlockSubsystem_Body, Resources.MsgNoUnlockSubsystem_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var numAdditionalArmSlots = gameState.FindOrCreateField<IntPropertyViewModel>("mNumTotalArmEquipmentSlots");

            var dialog = new CheatInventoryWindow //reusing CheatInventory since it's the same kind of prompt
            {
                Owner = Application.Current.MainWindow
            };
            var cvm = (CheatInventoryViewModel)dialog.DataContext;
            cvm.NumberChosen = numAdditionalArmSlots.Value;
            cvm.OldSlotsDisplay = numAdditionalArmSlots.Value;
            dialog.ShowDialog();

            if (cvm.NumberChosen < 0 || cvm.NumberChosen == numAdditionalArmSlots.Value)
            {
                MessageBox.Show(Resources.MsgArmSlotsUnchanged_Body, Resources.MsgUnchanged_Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            numAdditionalArmSlots.Value = cvm.NumberChosen;
            string message = string.Format(Resources.MsgArmSlotsSet_Body, cvm.NumberChosen);
            if (numAdditionalArmSlots.Value > 6)
                message += Resources.MsgArmSlotsVisualWarning_Body;
            MessageBox.Show(message, Resources.MsgSuccess_Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}
