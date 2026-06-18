using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.View;
using SatisfactorySaveEditor.ViewModel;
using SatisfactorySaveEditor.ViewModel.Property;
using SatisfactorySaveEditor.ViewModel.Struct;
using SatisfactorySaveParser;
using SatisfactorySaveParser.PropertyTypes.Structs;

namespace SatisfactorySaveEditor.Model
{
    public class SaveComponentModel : SaveObjectModel
    {
        private string parentEntityName;

        public string ParentEntityName
        {
            get => parentEntityName;
            set { SetProperty(ref parentEntityName, value, nameof(ParentEntityName)); }
        }

        private RelayCommand fillInventoryCommand;
        private RelayCommand emptyInventoryCommand;

        // 1.0+ raw V2 コンポーネント（DataFields==null）は mInventoryStacks を持たず Inventory==null。
        // その場合コマンドを無効化し、Fill/Empty 実行時の inv.Elements 参照クラッシュを防ぐ。
        // WPF バインディングはコマンドの同一性を前提とするため、プロパティアクセスごとに new せず
        // 遅延初期化で 1 度だけ生成して使い回す（CanExecuteChanged 伝播・割り当て削減のため）。
        public RelayCommand FillInventoryCommand => fillInventoryCommand ??= new RelayCommand(FillInventory, () => Inventory != null);

        public RelayCommand EmptyInventoryCommand => emptyInventoryCommand ??= new RelayCommand(EmptyInventory, () => Inventory != null);

        public SaveComponentModel(SaveComponent sc) : base(sc)
        {
            ParentEntityName = sc.ParentEntityName;
        }

        public override void ApplyChanges()
        {
            base.ApplyChanges();

            var model = (SaveComponent)Model;

            model.ParentEntityName = ParentEntityName;
        }

        public ArrayPropertyViewModel Inventory
        {
           get => FindField<ArrayPropertyViewModel>("mInventoryStacks");
        }

        public override bool MatchesFilter(string filter)
        {
            return base.MatchesFilter(filter) || MatchesFilterInventory(filter);
        }

        private bool MatchesFilterInventory(string filter)
        {

            return Inventory?.Elements.Cast<StructPropertyViewModel>().Any(element =>
            {
                DynamicStructDataViewModel structData = (DynamicStructDataViewModel)element.StructData;
                InventoryItem item = (InventoryItem)((StructPropertyViewModel) structData.Fields[0]).StructData;

                return item.ItemType.ToLower(CultureInfo.InvariantCulture).Contains(filter);
            }) ?? false;
        }

        private void FillInventory()
        {
            if (Inventory == null) return; // raw V2 コンポーネント等で在庫プロパティが無い場合は何もしない（クラッシュ防止）
            FillWindow dialog = new FillWindow();
            FillViewModel fvm = (FillViewModel) dialog.DataContext;
            dialog.ShowDialog();

            if(!fvm.IsConfirmed) return;
            ArrayPropertyViewModel inv = this.Inventory;
            foreach (StructPropertyViewModel element in inv.Elements)
            {
                DynamicStructDataViewModel structData = (DynamicStructDataViewModel)element.StructData;
                InventoryItem item = (InventoryItem)((StructPropertyViewModel)structData.Fields[0]).StructData;
                IntPropertyViewModel numItems = (IntPropertyViewModel)structData.Fields[1];
                item.ItemType = fvm.SelectedItem.ItemPath;
                numItems.Value = fvm.SelectedItem.Quantity;
            }
            ApplyChanges();
        }

        private void EmptyInventory()
        {
            var inv = this.Inventory;
            if (inv == null) return; // raw V2 コンポーネント等で在庫プロパティが無い場合は何もしない（クラッシュ防止）
            foreach (StructPropertyViewModel element in inv.Elements)
            {
                DynamicStructDataViewModel structData = (DynamicStructDataViewModel)element.StructData;
                InventoryItem item = (InventoryItem)((StructPropertyViewModel)structData.Fields[0]).StructData;
                IntPropertyViewModel numItems = (IntPropertyViewModel)structData.Fields[1];
                item.ItemType = string.Empty;
                numItems.Value = 0;
            }
            ApplyChanges();
            MessageBox.Show(string.Format(Resources.MsgInventoryEmptied_Body, Title), Resources.MsgInventoryEmptied_Title);
        }
    }
}
