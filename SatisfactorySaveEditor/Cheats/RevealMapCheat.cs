using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.View;
using SatisfactorySaveEditor.ViewModel;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

using System;
using System.Windows;

namespace SatisfactorySaveEditor.Cheats
{
    public class RevealMapCheat : ICheat
    {
        public string NameKey => "CheatUncoverMap";

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var mapManager = rootItem.FindChild("Persistent_Level:PersistentLevel.MapManager", false);
            if (mapManager == null)
            {
                MessageBox.Show(Resources.MsgNoMapManager_Body, Resources.MsgNoMapManager_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            //map data is an Array(Byte) containing 1048576 elements ranging from 0 to 255, where 255 is fully revealed.
            var fogOfWarRawData = mapManager.FindOrCreateField<ArrayPropertyViewModel>("mFogOfWarRawData");

            if (!(fogOfWarRawData is ArrayPropertyViewModel))
            {
                MessageBox.Show(Resources.MsgFogOfWarWrongType_Body, Resources.MsgWrongPropertyType_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (fogOfWarRawData.Elements.Count != 1048576)
                MessageBox.Show(string.Format(Resources.MsgFogOfWarUnexpectedCount_Body, fogOfWarRawData.Elements.Count), Resources.MsgWarning_Title, MessageBoxButton.OK, MessageBoxImage.Warning);

            int mapRevealThreshold = 0;

            var dialog = new StringPromptWindow
            {
                Owner = Application.Current.MainWindow
            };
            var cvm = (StringPromptViewModel)dialog.DataContext;
            cvm.WindowTitle = Resources.PromptRevealMap_Title;
            cvm.PromptMessage = Resources.PromptRevealMap_Caption;
            cvm.ValueChosen = "255";
            cvm.OldValueMessage = Resources.PromptRevealMap_Detail;
            dialog.ShowDialog();

            try
            {
                mapRevealThreshold = int.Parse(cvm.ValueChosen);
                if (mapRevealThreshold >= 0 && mapRevealThreshold <= 255)
                {
                    for (int i = 0; i < fogOfWarRawData.Elements.Count; i++)
                    {
                        ((BytePropertyViewModel)fogOfWarRawData.Elements[i]).Value = $"{mapRevealThreshold}";
                    }
                }
                else
                {
                    MessageBox.Show(Resources.MsgRevealMapRange_Body);
                    return false;
                }
            }
            catch (Exception)
            {
                if (!(cvm.ValueChosen == "cancel"))
                {
                    MessageBox.Show(string.Format(Resources.MsgCouldNotParse_Body, cvm.ValueChosen));
                }
                return false;
            }

            MessageBox.Show(Resources.MsgMapDataUncovered_Body, Resources.MsgSuccess_Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
    }
}
