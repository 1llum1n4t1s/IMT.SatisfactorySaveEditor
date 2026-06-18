using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SatisfactorySaveEditor.Model;
using SatisfactorySaveParser;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Util;
using System.Windows;
using Microsoft.Win32;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GongSolutions.Wpf.DragDrop;
using SatisfactorySaveEditor.Cheats;
using SatisfactorySaveEditor.View;
using System.IO.Compression;
using System.Windows.Threading;
using AsyncAwaitBestPractices.MVVM;
using SuperLightLogger;
using System.ComponentModel;
using SatisfactorySaveEditor.Properties;

namespace SatisfactorySaveEditor.ViewModel
{
    public class MainViewModel : ObservableObject, IDropTarget
    {
        private SatisfactorySave saveGame;
        private SaveObjectModel rootItem;
        private SaveObjectModel selectedItem;
        private string searchText;
        private World3DWindow open3DWindow; // 開いている 3D ビュー（多重生成防止用。閉じたら null に戻す）
        private CancellationTokenSource tokenSource = new CancellationTokenSource();
        private ObservableCollection<SaveObjectModel> rootItems = new ObservableCollection<SaveObjectModel>();
        private static readonly ILog log = LogManager.GetCurrentClassLogger();

        private bool isBusyInternal = false;
        public bool IsBusy
        {
            get
            {
                return isBusyInternal;
            }
            set
            {
                SetProperty(ref isBusyInternal, value, nameof(IsBusy));
            }
        }

        public ObservableCollection<SaveObjectModel> RootItem
        {
            get => rootItems;
            private set { SetProperty(ref rootItems, value, nameof(RootItem)); }
        }

        public SaveObjectModel SelectedItem
        {
            get => selectedItem;
            set { SetProperty(ref selectedItem, value, nameof(SelectedItem)); }
        }

        public string FileName
        {
            get
            {
                if (saveGame == null) return string.Empty;
                return string.Format(" - {1} [{0}]", saveGame.FileName, saveGame.Header.SessionName);
            }
        }

        public string SearchText
        {
            get => searchText;
            set
            {
                SetProperty(ref searchText, value, nameof(SearchText));

                tokenSource.Cancel();
                tokenSource = new CancellationTokenSource();
                // 各検索タスクは自前の token を捕捉する。共有 tokenSource.Token を Filter 内で読むと
                // 古いタスクが新タスクの未キャンセル token を見て古い結果を反映しうるため。
                var filterToken = tokenSource.Token;
                Task.Factory.StartNew(() => Filter(value, filterToken), filterToken);
            }
        }

        public ObservableCollection<string> LastFiles { get; } = new ObservableCollection<string>();

        public ObservableCollection<ICheat> CheatMenuItems { get; } = new ObservableCollection<ICheat>();

        public RelayCommand<SaveObjectModel> TreeSelectCommand { get; }
        public RelayCommand<string> JumpCommand { get; }
        public RelayCommand JumpMenuCommand { get; }
        public RelayCommand<CancelEventArgs> ExitCommand { get; }
        public RelayCommand<string> OpenCommand { get; }
        public RelayCommand Help_ViewGithubCommand { get; }
        public RelayCommand Help_ReportIssueCommand { get; }
        public RelayCommand Help_RequestHelpDiscordCommand { get; }
        public RelayCommand Help_FicsitAppGuideCommand { get; }
        public RelayCommand AboutCommand { get; }
        public RelayCommand<SaveObjectModel> DeleteCommand { get; }
        public RelayCommand<ICheat> CheatCommand { get; }
        public AsyncCommand<bool> SaveCommand { get; }
        public AsyncCommand ManualBackupCommand { get; }
        public RelayCommand ResetSearchCommand { get; }
        public RelayCommand CheckUpdatesCommand { get; }
        public RelayCommand PreferencesCommand { get; }
        public RelayCommand Open3DViewCommand { get; }

        public bool HasUnsavedChanges { get; set; } //TODO: set this to true when any value in WPF is changed. current plan for this according to goz3rr is to make a wrapper for the data from the parser and then change the set method in the wrapper

        public MainViewModel()
        {
            string[] args = Environment.GetCommandLineArgs();
            // 起動引数のセーブは全 command 初期化後（ctor 末尾）にロードする。ここで即 LoadFile すると
            // LoadFileAsync が未初期化の SaveCommand 等を参照して失敗するため、パスだけ保持しておく。
            var initialFile = args.Length > 1 && File.Exists(args[1]) ? args[1] : null;

            var savedFiles = Properties.Settings.Default.LastSaves?.Cast<string>().ToList();
            
            if(savedFiles != null)
            {
                bool modified = false;
                foreach (string filePath in savedFiles) //silently remove files that no longer exist from the list in Properties
                {
                    if (!File.Exists(filePath))
                    {
                        modified = true;
                        log.Info($"Removing save file {filePath} from recent saves list since it wasn't found on disk");
                        Properties.Settings.Default.LastSaves.Remove(filePath);
                    }
                }
                if (modified) //regenerate list since a save was not found when first built
                    savedFiles = Properties.Settings.Default.LastSaves?.Cast<string>().ToList();
                LastFiles = new ObservableCollection<string>(savedFiles);
            } 
            else //create a new empty collection for the list since there isn't anything there
                LastFiles = new ObservableCollection<string>();

            // TODO: load this dynamically
            CheatMenuItems.Add(new ResearchUnlockCheat());
            CheatMenuItems.Add(new UnlockMapCheat());
            CheatMenuItems.Add(new RevealMapCheat());
            CheatMenuItems.Add(new InventorySlotsCheat()); //inventory slot count works again (but is in a different place) as of Update 3
            CheatMenuItems.Add(new ArmSlotsCheat()); //inventory slot count works again (but is in a different place) as of Update 3
            CheatMenuItems.Add(new KillPlayersCheat());
            CheatMenuItems.Add(new CouponChangerCheat());
            DeleteEnemiesCheat deleteEnemiesCheat = new DeleteEnemiesCheat();
            CheatMenuItems.Add(deleteEnemiesCheat);
            CheatMenuItems.Add(new UndoDeleteEnemiesCheat());
            CheatMenuItems.Add(new SpawnDoggoCheat(deleteEnemiesCheat));
            CheatMenuItems.Add(new MassDismantleCheat());
            CheatMenuItems.Add(new EverythingBoxCheat());
            CheatMenuItems.Add(new CrateSummonCheat());
            CheatMenuItems.Add(new NoCostCheat());
            CheatMenuItems.Add(new NoPowerCheat());
            CheatMenuItems.Add(new RemoveSlugsCheat());
            CheatMenuItems.Add(new RestoreSlugsCheat());
            CheatMenuItems.Add(new DeduplicateSchematicsCheat());

            TreeSelectCommand = new RelayCommand<SaveObjectModel>(SelectNode);
            JumpCommand = new RelayCommand<string>(Jump, CanJump);
            JumpMenuCommand = new RelayCommand(JumpMenu, () => CanSave(false)); //disallow menu jumping if no save is loaded
            ExitCommand = new RelayCommand<CancelEventArgs>(Exit);
            OpenCommand = new RelayCommand<string>(async (fileName) => await Open(fileName) );
            AboutCommand = new RelayCommand(About);
            Help_ViewGithubCommand = new RelayCommand(Help_ViewGithub);
            Help_ReportIssueCommand = new RelayCommand(Help_ReportIssue);
            Help_RequestHelpDiscordCommand = new RelayCommand(Help_RequestHelpDiscord);
            Help_FicsitAppGuideCommand = new RelayCommand(Help_FicsitAppGuide);
            CheckUpdatesCommand = new RelayCommand(() =>
            {
                CheckForUpdate(true).ConfigureAwait(false);
            });
            PreferencesCommand = new RelayCommand(OpenPreferences);
            Open3DViewCommand = new RelayCommand(Open3DView);

            DeleteCommand = new RelayCommand<SaveObjectModel>(Delete, CanDelete);
            SaveCommand = new AsyncCommand<bool>(async (saveAs) => await Save(saveAs), CanSave, null, true);
            ManualBackupCommand = new AsyncCommand(async () => await CreateBackup(true), CanSave);
            CheatCommand = new RelayCommand<ICheat>(Cheat, CanCheat);
            ResetSearchCommand = new RelayCommand(ResetSearch);

            CheckForUpdate(false).ConfigureAwait(false);

            // 全 command 初期化後に起動引数のセーブをロードする（fire-and-forget、例外は LoadFileAsync 内で処理済み）。
            if (initialFile != null)
            {
                _ = LoadFile(initialFile);
            }
        }

        private void OpenPreferences()
        {
            var window = new PreferencesWindow
            {
                Owner = Application.Current.MainWindow
            };

            window.ShowDialog();
        }

        /// <summary>
        ///     3D ワールドビューを非モーダルで開く（ツリーと並行して使える）。セーブ未ロードなら何もしない。
        /// </summary>
        private void Open3DView()
        {
            // 既に 3D ビューが開いていれば多重生成せず最前面化する。
            // 連打で DX11 デバイス + 頂点バッファが個数分リークするのを防ぐ。
            if (open3DWindow != null)
            {
                open3DWindow.Activate();
                return;
            }

            if (saveGame?.Entries == null || rootItem == null)
            {
                MessageBox.Show(Resources.Msg3DNoSave_Body, Resources.Menu3DView, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 未保存のツリー編集（削除/複製）を反映するため、Save 時まで stale な saveGame.Entries ではなく
            // 現在のツリー状態（Save の Entries 再構成と同じ rootItem.DescendantSelf）から構築する。
            // これにより、削除済みアクターが 3D ビューに残って Ctrl+D でツリーに復活する不整合を防ぐ。
            var currentObjects = rootItem.DescendantSelf.ToList();
            if (currentObjects.Count == 0)
            {
                MessageBox.Show(Resources.Msg3DNoSave_Body, Resources.Menu3DView, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new World3DWindow(currentObjects)
            {
                Owner = Application.Current.MainWindow
            };
            open3DWindow = window;
            window.Closed += (s, e) => open3DWindow = null;
            window.Show();
        }

        private async Task CheckForUpdate(bool manual)
        {
            if (!manual && !Properties.Settings.Default.AutoUpdate) return;

            var latestVersion = await UpdateChecker.GetLatestReleaseInfo();

            if (latestVersion != null && latestVersion.IsNewer())
            {
                UpdateWindow window = new UpdateWindow
                {
                    DataContext = new UpdateWindowViewModel(latestVersion),
                    Owner = Application.Current.MainWindow
                };

                window.ShowDialog();
            }
            else if (manual)
            {
                MessageBox.Show(Resources.MsgAlreadyLatestVersion_Body, Resources.MsgUpdate_Title, MessageBoxButton.OK);
            }
        }

        /// <summary>
        /// Checks if the passed model is not the rootItem of the save
        /// </summary>
        /// <param name="model"></param>
        /// <returns>If deletion is allowed</returns>
        private bool CanDelete(SaveObjectModel model)
        {
            return model != rootItem;
        }

        /// <summary>
        /// Removes the passed model from rootItem and raises property changed on the root item.
        /// </summary>
        /// <param name="model">The model to delete</param>
        private void Delete(SaveObjectModel model)
        {
            rootItem.Remove(model);
            OnPropertyChanged(nameof(RootItem));
        }

        /// <summary>
        /// Checks if rootItem exists (if a save file is opened)
        /// </summary>
        /// <param name="cheat"></param>
        /// <returns>True if the root item is NOT null, false otherwise</returns>
        private bool CanCheat(ICheat cheat)
        {
            return rootItem != null;
        }

        /// <summary>
        /// Calls Apply() on the passed ICheat, providing it with rootItem. Only mark for unsaved changes if the cheat succeeds.
        /// </summary>
        /// <param name="cheat">The cheat to run</param>
        private void Cheat(ICheat cheat)
        {
            
            log.Info($"Applying cheat {cheat.GetType().Name}");

            // 1.0+ セーブでは全オブジェクトが RawData 保持（DataFields==null）でプロパティ未パースのため、
            // スラグ系（CollectedObjects 未書き出し）もプロパティ系（FindField が null を返し NRE）も機能しない。
            // V2 プロパティ対応（Stage3）まで 1.0 では全チートをブロックする。
            if (saveGame?.Header != null && saveGame.Header.IsNewFormat)
            {
                System.Windows.MessageBox.Show(
                    SatisfactorySaveEditor.Properties.Resources.MsgCheatUnsupportedV2_Body,
                    SatisfactorySaveEditor.Properties.Resources.MsgCheatUnsupportedV2_Title,
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            if (cheat.Apply(rootItem, saveGame))
                HasUnsavedChanges = true;
        }

        /// <summary>
        /// Checks if the editor can perform a save operation
        /// </summary>
        /// <param name="saveAs">If the save operation is Save As (unused)</param>
        /// <returns>True if saveGame is NOT null, false otherwise</returns>
        private bool CanSave(object _)
        {
            return saveGame != null;
        }

        private bool CanSave() //overload of CanSave(bool saveAs) for contexts when saveAs doesn't matter
        {
            return CanSave(false);
        }

        /// <summary>
        /// Save changes, creating a backup first if auto backups are enabled in the user's preferences
        /// </summary>
        /// <param name="saveAs">(optional) If the Save As... option box should be brought up to choose a destination</param>
        private async Task Save(bool saveAs)
        {
            string targetFile = null;
            if (saveAs)
            {
                SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = Resources.FileFilterSaveGame,
                    InitialDirectory = Path.GetDirectoryName(saveGame.FileName),
                    DefaultExt = ".sav",
                    CheckFileExists = false,
                    AddExtension = true
                };

                if (dialog.ShowDialog() != true) return;
                targetFile = dialog.FileName;
            }

            try
            {
                await AutoBackupIfEnabled();

                var newObjects = rootItem.DescendantSelf;
                saveGame.Entries = saveGame.Entries.Intersect(newObjects).ToList();
                saveGame.Entries.AddRange(newObjects.Except(saveGame.Entries));

                rootItem.ApplyChanges();
                this.IsBusy = true;
                if (targetFile != null)
                    await Task.Run(() => saveGame.Save(targetFile));
                else
                    await Task.Run(() => saveGame.Save());

                HasUnsavedChanges = false;
                if (targetFile != null)
                {
                    OnPropertyChanged(nameof(FileName));
                    AddRecentFileEntry(targetFile);
                }
            }
            catch (Exception ex)
            {
                // 保存失敗を握りつぶすとユーザーは編集が永続化されたと誤認する。明示的に通知する。
                // SatisfactorySave.Save は memory-first（完全シリアライズ後に書き出し）なので、
                // ここで例外が出ても元ファイルは無傷のまま残る。
                log.Error(ex);
                MessageBox.Show(string.Format(Resources.MsgSaveFailed_Body, ex.Message), Resources.MsgSaveFailed_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        private async Task AutoBackupIfEnabled()
        {
            if (Properties.Settings.Default.AutoBackup)
            {
                await CreateBackup(false);
            }
        }

        private async Task CreateBackup(bool manual)
        {
            this.IsBusy = true;
            await Task.Run(() => CreateBackupAsync(manual));
            this.IsBusy = false;
        }

        private void CreateBackupAsync(bool manual)
        {  
            string saveFileDirectory = Path.GetDirectoryName(saveGame.FileName);
            string tempDirectoryName = @"\SSEtemp\";
            string pathToZipFrom = saveFileDirectory + tempDirectoryName;

            string tempFilePath = saveFileDirectory + tempDirectoryName + Path.GetFileName(saveGame.FileName);
            string backupFileFullPath = saveFileDirectory + @"\" + Path.GetFileNameWithoutExtension(saveGame.FileName) + "_" + DateTimeOffset.Now.ToUnixTimeMilliseconds() + ".SSEbkup.zip";

            log.Info($"Creating a {(manual ? "manual " : "")}backup for {saveGame.FileName}");

            try
            {
                //Satisfactory save files compress exceedingly well, so compress all backups so that they take up less space.
                //ZipFile only accepts directories, not single files, so copy the save to a temporary folder and then zip that folder
                Directory.CreateDirectory(pathToZipFrom);
                File.Copy(saveGame.FileName, tempFilePath, true); 
                ZipFile.CreateFromDirectory(pathToZipFrom, backupFileFullPath);
            }
            catch (Exception ex)
            {
                //should never be reached, but hopefully any users that encounter an error here will report it 
                MessageBox.Show(Resources.MsgBackupError_Body);
                log.Error(ex);
                throw;
            }
            finally
            {
                // 圧縮が失敗してもテンポラリを片付ける。ただし片付けの二次例外が catch で throw した真の例外を
                // 覆い隠さないよう、削除自体を保護する。非再帰 Delete は中身が残ると例外になるため recursive で一括削除。
                try
                {
                    if (Directory.Exists(pathToZipFrom))
                        Directory.Delete(pathToZipFrom, true);
                }
                catch (Exception cleanupEx)
                {
                    log.Error(cleanupEx); // クリーンアップ失敗は副次的。本来の例外を優先するためログのみに留める
                }
            }

            if (manual)
                MessageBox.Show(Resources.MsgBackupCreated_Body);
        }

        /// <summary>
        /// Checks if it's possible to jump to the passed EntityName string
        /// </summary>
        /// <param name="target">The EntityName to jump to, in string format</param>
        /// <returns>True if rootItem contains the EntitiyName, false otherwise.</returns>
        private bool CanJump(string target)
        {
            return rootItem?.FindChild(target, false) != null;
        }

        /// <summary>
        /// Opens the Github repo page scrolled to the 'Help' heading
        /// </summary>
        private void Help_ViewGithub()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Goz3rr/SatisfactorySaveEditor#help") { UseShellExecute = true });
        }

        /// <summary>
        /// Opens the Github repo page scrolled to the Issues tab
        /// </summary>
        private void Help_ReportIssue()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Goz3rr/SatisfactorySaveEditor/issues") { UseShellExecute = true });
        }

        /// <summary>
        /// Notifies the user of their redirection to the discord, then opens the invite.
        /// </summary>
        private void Help_RequestHelpDiscord()
        {
            MessageBox.Show(Resources.MsgDiscordRedirect_Body);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://bit.ly/SatisfactoryModding") { UseShellExecute = true }); //discord invite for Satisfactory Modding server. Contact BaineGames#7333 if it breaks
        }

        /// <summary>
        /// Notifies the user of their redirection to the ficsit.app guide, then opens the guide.
        /// </summary>
        private void Help_FicsitAppGuide()
        {
            MessageBox.Show(Resources.MsgFicsitRedirect_Body);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://ficsit.app/guide/Z8h6z2CczH43c") { UseShellExecute = true });
        }


        /// <summary>
        /// Displays version information box
        /// </summary>
        private void About()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            MessageBox.Show($"{Resources.MsgAboutEditorName_Body}{Environment.NewLine}{version}", Resources.MsgAbout_Title);
        }

        /// <summary>
        /// Starts the process of loading a file, prompting the user if there are unsaved changes. Marks as having no unsaved changes
        /// </summary>
        /// <param name="fileName">Path to the save file</param>
        private async Task Open(string fileName)
        {
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                await LoadFile(fileName);
                HasUnsavedChanges = false;

                return;
            }

            if (HasUnsavedChanges)
            {
                MessageBoxResult result = MessageBox.Show(Resources.MsgUnsavedChangesOpen_Body, Resources.MsgUnsavedChanges_Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            //TODO: swap this over to calling the save method instead
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = Resources.FileFilterSaveGame
            };

            var newPath = Environment.ExpandEnvironmentVariables(@"%localappdata%\FactoryGame\Saved\SaveGames\");
            var oldPath = Environment.ExpandEnvironmentVariables(@"%userprofile%\Documents\My Games\FactoryGame\SaveGame\");

            if (Directory.Exists(newPath)) dialog.InitialDirectory = newPath;
            else dialog.InitialDirectory = oldPath;

            if (dialog.ShowDialog() == true)
            {
                await LoadFile(dialog.FileName);
                HasUnsavedChanges = false;
            }
        }

        /// <summary>
        /// Checks if there are unsaved changes, exits otherwise or if the user choses to discard.
        /// TODO: Mark as unsaved when property fileds are changed
        /// TODO: Check this when pressing alt+f4 and clicking the red x
        /// </summary>
        private void Exit(CancelEventArgs args = null)
        {
            if (HasUnsavedChanges)
            {
                MessageBoxResult result = MessageBox.Show(Resources.MsgUnsavedChangesClose_Body, Resources.MsgUnsavedChanges_Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    if (args != null) args.Cancel = true;
                }
            }
            else
            {
                Application.Current.Shutdown();
            }

        }

        /// <summary>
        /// Select the specified entity in the tree view
        /// </summary>
        /// <param name="target">EntityName of the entity to jump to</param>
        private void Jump(string target)
        {
            if(SelectedItem != null)
                SelectedItem.IsSelected = false;
            SelectedItem = rootItem.FindChild(target, true);
        }

        /// <summary>
        /// 3D ビュー等の外部から、インスタンス名でツリー選択を駆動する公開エントリ。
        /// 右ペイン（SelectedItem バインド）を更新し、ツリーのハイライト＋祖先展開も行う。
        /// </summary>
        /// <param name="instanceName">対象 SaveObject の InstanceName（= SaveObjectModel.Title）</param>
        /// <returns>選択できた場合 true</returns>
        public bool SelectByInstanceName(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName) || rootItem == null)
                return false;

            var target = rootItem.FindChild(instanceName, true); // ヒット時 IsSelected=true・祖先 IsExpanded=true
            if (target == null)
                return false;

            if (SelectedItem != null && !ReferenceEquals(SelectedItem, target))
                SelectedItem.IsSelected = false;
            SelectedItem = target; // 右ペイン（PROPERTIES）を更新
            return true;
        }

        /// <summary>
        /// 3D ビュー等の外部から、インスタンス名でオブジェクトを削除する。既存 Delete（:252）と同じく
        /// ツリーから外すだけで、Save 時に rootItem.DescendantSelf との Intersect で Entries から自動 prune される。
        /// </summary>
        public bool DeleteByInstanceName(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName) || rootItem == null)
                return false;

            var m = rootItem.FindChild(instanceName, false);
            if (m == null || m == rootItem)
                return false;

            rootItem.Remove(m);
            if (SelectedItem == m) SelectedItem = null; // 右ペインをクリア
            OnPropertyChanged(nameof(RootItem));
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>SaveObject 実体（identity）から対応する Model ノードを再帰探索する。Title（リネームで変わる）でなく
        /// 参照一致で引くため、プロパティペインでのリネーム後に InstanceName が旧のままでも正しく一致する。</summary>
        private static SaveObjectModel FindNodeByModel(SaveObjectModel root, SatisfactorySaveParser.SaveObject target)
        {
            if (root == null) return null;
            if (ReferenceEquals(root.Model, target)) return root;
            foreach (var child in root.Items)
            {
                var found = FindNodeByModel(child, target);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>SaveObject 実体（identity）で削除する。3D ビューは SaveEntity 参照を保持するため、リネーム未適用
        /// （Title 変更済み・InstanceName 旧のまま）でも確実にツリーから prune できる。</summary>
        public bool DeleteByEntity(SatisfactorySaveParser.SaveObject target)
        {
            if (target == null || rootItem == null) return false;
            // コンポーネント／親／参照を持つアクターを 3D 削除すると、TypePath で別グループに並ぶコンポーネントが
            // 存在しないアクターを指す OuterPathName で書き戻され孤児化する。完全な参照 prune は Stage3 まで未対応
            // なので、参照を持つアクターの削除は拒否する（複製ガードと対称）。
            if (target is SatisfactorySaveParser.SaveEntity refEnt && refEnt.HasOutgoingReferences())
            {
                MessageBox.Show(Resources.MsgDeleteRefUnsupported_Body, Resources.MsgDeleteRefUnsupported_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var m = FindNodeByModel(rootItem, target);
            if (m == null || m == rootItem) return false;
            rootItem.Remove(m);
            if (SelectedItem == m) SelectedItem = null;
            OnPropertyChanged(nameof(RootItem));
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// 3D ビュー等の外部から、インスタンス名でオブジェクトを複製する。新しい一意 InstanceName を採番し、
        /// 1.0 本体（RawData）は逐語コピーするので byte-exact round-trip。Position は +offsetXcm ずらす。
        /// クローンを Entries とツリー両方に追加し、複製した SaveEntity を返す（3D 側で立方体追加・選択に使う）。
        /// </summary>
        public SaveEntity DuplicateByInstanceName(string instanceName, float offsetXcm = 300f)
        {
            if (string.IsNullOrEmpty(instanceName) || saveGame?.Entries == null || rootItem == null)
                return null;

            var src = saveGame.Entries.OfType<SaveEntity>().FirstOrDefault(e => e.InstanceName == instanceName);
            if (src == null) return null;

            // 3D ビューを開いている間にツリーから削除されたアクターは Entries に残るが live なツリーノードを失う。
            // その状態で複製すると削除済みオブジェクトを復活させてしまうため、ツリーノードが残っている時だけ複製する。
            if (rootItem.FindChild(instanceName, false) == null) return null;

            // P0 止血: 参照を持つアクターを複製するとクローンと原本が同一コンポーネント／親を共有してゲームロードで破損する。
            // 1.0+ の raw アクターは Components が未展開なので、RawData 先頭の data プレフィックス＋プロパティを読んで
            // 参照の有無を判定する（SaveEntity.HasOutgoingReferences。電線等は "None" 整合確認＋ObjectProperty 検出の
            // 二重防御で検出）。参照なし（Foundation 等）は複製を許可し、参照あり（コンポーネント／親／プロパティ参照）は
            // 拒否して通知する。旧 ID→新 ID の参照書き換えは Stage3 の V2 ライターに依存（それまで複製は参照なしに限定）。
            if (src.HasOutgoingReferences())
            {
                MessageBox.Show(Resources.MsgDuplicateRefUnsupported_Body, Resources.MsgDuplicateRefUnsupported_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var clone = new SaveEntity(src.TypePath, src.RootObject, MakeUniqueInstanceName(src.InstanceName))
            {
                ObjectFlags = src.ObjectFlags,
                NeedTransform = src.NeedTransform,
                WasPlacedInLevel = src.WasPlacedInLevel,
                DataSaveVersion = src.DataSaveVersion,
                ShouldMigrate = src.ShouldMigrate,
                OptionalVersionData = src.OptionalVersionData, // byte[]（編集しないので参照コピー可）
                RawData = src.RawData,                         // 1.0 本体は逐語コピー（InstanceName は RawData に無い）
                DataFields = src.DataFields?.DeepClone(saveGame.Header.BuildVersion),                   // 旧形式用（1.0 では null）
                ParentObjectRoot = src.ParentObjectRoot,
                ParentObjectName = src.ParentObjectName,
                Components = src.Components != null
                    ? new System.Collections.Generic.List<SatisfactorySaveParser.Structures.ObjectReference>(src.Components)
                    : new System.Collections.Generic.List<SatisfactorySaveParser.Structures.ObjectReference>(),
            };

            // Vector はクラス＝共有回避のため必ず new する（共有すると原本も動く）。未設定（null）なら複製側も未設定のまま。
            var r = src.Rotation;
            if (r != null) clone.Rotation = new SatisfactorySaveParser.Structures.Vector4 { X = r.X, Y = r.Y, Z = r.Z, W = r.W };
            var s = src.Scale;
            if (s != null) clone.Scale = new SatisfactorySaveParser.Structures.Vector3 { X = s.X, Y = s.Y, Z = s.Z };
            var p = src.Position;
            if (p != null) clone.Position = new SatisfactorySaveParser.Structures.Vector3 { X = p.X + offsetXcm, Y = p.Y, Z = p.Z };

            // パーサ Entries に追加（保存の真実）。
            saveGame.Entries.Add(clone);

            // エディタツリーにも追加（DescendantSelf に乗せ、Save 時の Except 分岐で Entries 整合）。原本と同じ親の下へ。
            var srcNode = rootItem.FindChild(src.InstanceName, false);
            var parent = (srcNode != null ? FindParentOf(rootItem, srcNode) : null) ?? rootItem;
            parent.Items.Add(new SaveEntityModel(clone));

            OnPropertyChanged(nameof(RootItem));
            HasUnsavedChanges = true;
            return clone;
        }

        /// <summary>ツリーを再帰探索し、target を直接の子に持つ親ノードを返す（SaveObjectModel に Parent が無いため）。</summary>
        private static SaveObjectModel FindParentOf(SaveObjectModel root, SaveObjectModel target)
        {
            if (root == null) return null;
            foreach (var child in root.Items)
            {
                if (ReferenceEquals(child, target)) return root;
                var found = FindParentOf(child, target);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>末尾の数値 id を置換し、既存 InstanceName と衝突しない一意名を返す（衝突は in-game の参照解決を壊す）。</summary>
        private string MakeUniqueInstanceName(string original)
        {
            var existing = new System.Collections.Generic.HashSet<string>(saveGame.Entries.Select(e => e.InstanceName));
            string prefix; long start;
            int us = original.LastIndexOf('_');
            if (us >= 0 && long.TryParse(original.Substring(us + 1), out start))
                prefix = original.Substring(0, us + 1);
            else { prefix = original + "_"; start = 0; }
            for (long i = start + 1; ; i++)
            {
                var candidate = prefix + i.ToString(CultureInfo.InvariantCulture);
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        /// <summary>
        /// Opens a StringPromptWindow prompting for an EntityName to jump to
        /// </summary>
        private void JumpMenu()
        {
            string destination = "";

            var dialog = new StringPromptWindow
            {
                Owner = Application.Current.MainWindow
            };
            var cvm = (StringPromptViewModel)dialog.DataContext;
            cvm.WindowTitle = Resources.PromptJumpToTag_Title;
            cvm.PromptMessage = Resources.PromptJumpToTag_Caption;
            cvm.ValueChosen = "";
            cvm.OldValueMessage = Resources.PromptJumpToTag_Detail;
            dialog.ShowDialog();

            destination = cvm.ValueChosen;

            if(!(destination.Equals("") || destination.Equals("cancel")))
                if (CanJump(destination))
                    Jump(destination);
                else
                    MessageBox.Show(Resources.MsgJumpToTagFailed_Body + destination);
        }

        /// <summary>
        /// Selects a node
        /// </summary>
        /// <param name="node">The node to select</param>
        private void SelectNode(SaveObjectModel node)
        {
            SelectedItem = node;
        }

        /// <summary>
        /// Loads a file into the editor
        /// </summary>
        /// <param name="path">The path to the file to open</param>
        private async Task LoadFile(string path)
        {
            SelectedItem = null;
            SearchText = null;

            this.IsBusy = true;
            await Task.Run(() => LoadFileAsync(path));
            this.IsBusy = false;
        }

        private void LoadFileAsync(string path)
        {
            try
            {
                saveGame = new SatisfactorySave(path);
            }
            catch (FileNotFoundException)
            {
                if (LastFiles != null && LastFiles.Contains(path)) //if the save file that failed to open was on the last found list, remove it. this should only occur when people move save files around and leave the editor open.
                {
                    MessageBox.Show(Resources.MsgRecentFileMissing_Body, Resources.MsgFileNotPresent_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    log.Info($"Removing save file {path} from recent saves list since it wasn't found on disk");
                    Properties.Settings.Default.LastSaves.Remove(path);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LastFiles.Remove(path);
                    });
                }
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.MsgOpenFileError_Body, ex.Message), Resources.MsgErrorOpeningFile_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                log.Error(ex);
                return;
            }
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                //manually raise these for the AsyncCommand library to pick up on it (ask virusek20 or Robb)
                SaveCommand.RaiseCanExecuteChanged();
                ManualBackupCommand.RaiseCanExecuteChanged();
                //CommunityToolkit の RelayCommand は CommandManager 連動が無いため、セーブ読み込み後に明示的に再評価する
                JumpMenuCommand.NotifyCanExecuteChanged();
                JumpCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                CheatCommand.NotifyCanExecuteChanged();
            });
            
            rootItem = new SaveRootModel(saveGame.Header);
            var saveTree = new EditorTreeNode("Root");

            foreach (var entry in saveGame.Entries)
            {
                var parts = entry.TypePath.TrimStart('/').Split('/');
                saveTree.AddChild(parts, entry);
            }

            BuildNode(rootItem.Items, saveTree);

            rootItem.IsExpanded = true;
            foreach (var item in rootItem.Items)
            {
                item.IsExpanded = true;
            }

            OnPropertyChanged(nameof(RootItem));
            OnPropertyChanged(nameof(FileName));

            AddRecentFileEntry(path);
        }

        /// <summary>
        /// Adds a recently opened file to the list
        /// </summary>
        /// <param name="path">The path of the file to add</param>
        private void AddRecentFileEntry(string path)
        {
            if (Properties.Settings.Default.LastSaves == null)
            {
                Properties.Settings.Default.LastSaves = new StringCollection();
            }

            if (LastFiles.Contains(path)) // No duplicates
            {
                Properties.Settings.Default.LastSaves.Remove(path);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LastFiles.Remove(path);
                });
                
            }

            Properties.Settings.Default.LastSaves.Add(path);
            Application.Current.Dispatcher.Invoke(() =>
            {
                LastFiles.Add(path);

                while (Properties.Settings.Default.LastSaves.Count >= 6) // Keeps only 5 most recent saves
                {
                    LastFiles.RemoveAt(0);
                    Properties.Settings.Default.LastSaves.RemoveAt(0);
                }
            });

            Properties.Settings.Default.Save();

            Application.Current.Dispatcher.Invoke(() =>
            {
                RootItem.Clear();
                RootItem.Add(rootItem);
            });
        }

        private void BuildNode(ObservableCollection<SaveObjectModel> items, EditorTreeNode node)
        {
            foreach (var child in node.Children)
            {
                var childItem = new SaveObjectModel(child.Value.Name);
                BuildNode(childItem.Items, child.Value);
                items.Add(childItem);
            }

            foreach (var entry in node.Content)
            {
                switch (entry)
                {
                    case SaveEntity se:
                        items.Add(new SaveEntityModel(se));
                        break;
                    case SaveComponent sc:
                        items.Add(new SaveComponentModel(sc));
                        break;
                }
            }
        }

        private void Filter(string value, CancellationToken cancellationToken)
        {
            if (rootItem == null) return;

            if (string.IsNullOrWhiteSpace(value))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RootItem.Clear();
                    RootItem.Add(rootItem);
                });
            }
            else
            {
                var valueLower = value.ToLower(CultureInfo.InvariantCulture);
                try
                {
                    // 背景スレッドで列挙を完了させてから（UI を塞がない）、UI スレッドへは確定済みリストだけ渡す。
                    // Dispatcher 内での遅延列挙中に 3D 操作でツリーが変わって並行例外になる窓を無くす。
                    var filtered = rootItem.DescendantSelfViewModelLazy
                        .WithCancellation(cancellationToken)
                        .Where(vm => vm.MatchesFilter(valueLower))
                        .ToList();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RootItem = new ObservableCollection<SaveObjectModel>(filtered);
                    });
                }
                catch (OperationCanceledException)
                {
                    // 新しい検索語に置き換わった（tokenSource.Cancel）。古い列挙は破棄してよい。
                }
                catch (InvalidOperationException)
                {
                    // 列挙中に 3D 操作等でツリーが変更された。次の打鍵で再評価されるため無視する。
                }
            }
        }

        private void ResetSearch()
        {
            SearchText = null;
        }

        public void DragOver(IDropInfo dropInfo)
        {
            if (!(dropInfo.Data is DataObject data)) return;

            var files = data.GetFileDropList();
            if (files == null || files.Count == 0) return;

            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Copy;
        }

        /// <summary>
        /// Handle drag and drop opening of save files
        /// </summary>
        /// <param name="dropInfo"></param>
        public void Drop(IDropInfo dropInfo)
        {
            var fileName = ((DataObject)dropInfo.Data).GetFileDropList()[0];
            _ = LoadFile(fileName);
            // No need to wait for it to finish, since that blocks the dispatcher thread and causes a deadlock
        }
    }
}