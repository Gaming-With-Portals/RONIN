using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RONIN.Browser;
using RONIN.Exporters;
using RONIN.Modding;
using RONIN.Preview;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.Pkcs;
using System.Windows;
using System.Windows.Input;
using YakumoLib.Assets;
using YakumoLib.Database;
using static HelixToolkit.SharpDX.Model.Metadata;
using System.ComponentModel;
using System.Windows.Data;

namespace RONIN;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal = 1;
    [ObservableProperty] private string _activePath = "/";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showTextures = true;
    [ObservableProperty] private bool _showMesh = true;
    [ObservableProperty] private bool _showVfx = true;
    [ObservableProperty] private bool _showAudio = true;
    [ObservableProperty] private bool _showData = true;
    [ObservableProperty] private bool _hideEmptyContainers = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedEntry))] private AssetEntry _selectedEntry = null;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasBackEntry))] private AssetEntry _previousEntry = null;

    public bool DoImportSound { get; set; }

    private readonly AssetPreviewRegistry _previewRegistry;
    private readonly AssetExporterRegistry _exporterRegistry;

    [ObservableProperty] private ICollectionView? _assetsView;
    //private readonly AssetImporterRegistry _importerRegistry;

    [ObservableProperty] private object? _previewContent;

    public bool HasSelectedEntry => SelectedEntry is not null;
    public bool HasBackEntry => PreviousEntry is not null;

    public bool CanGeneratePatch => true; // for now

    public AssetLibrary? Library { get; private set; }
    
    public ObservableCollection<FolderNodeViewModel> RootFolders { get; } = new();
    public ObservableCollection<AssetEntry> DebugAssets { get; } = new(); // TODO: Rename to current preview assets
    public ObservableCollection<ModifiedAssetEntry> ModifiedAssets { get; } = new();

    private SubAssetEntry _selectedSubEntry;
    public SubAssetEntry SelectedSubEntry
    {
        get => _selectedSubEntry;
        set
        {
            _selectedSubEntry = value;
            OnPropertyChanged();
        }
    }


    public MainWindowViewModel()
    {
        _previewRegistry = new AssetPreviewRegistry();
        _previewRegistry.Register(new TexturePreviewProvider());
        _previewRegistry.Register(new AtlasTexturePreviewProvider());
        _previewRegistry.Register(new AnimationPreviewProvider());
        _previewRegistry.Register(new FontPreviewProvider());
        _previewRegistry.Register(new ModelPreviewProvider());
        _previewRegistry.Register(new AssetTablePreviewProvider());

        _exporterRegistry = new AssetExporterRegistry();
        _exporterRegistry.Register(new ModelExportProvider());

        //_importerRegistry = new AssetImporterRegistry();
    }

    private void SetAssets(IEnumerable<AssetEntry> assets)
    {
        var list = new List<AssetEntry>(assets);
        var view = CollectionViewSource.GetDefaultView(list);
        view.Filter = FilterAsset;
        AssetsView = view;
    }

    private bool IsTypeVisible(AssetEntry asset) => asset.Type switch
    {
        AssetType.Texture or AssetType.AtlasTexture or AssetType.AnimationTexture or AssetType.Texture3D => ShowTextures,
        AssetType.SkeletalMesh or AssetType.StaticMesh or AssetType.StaticCollision or AssetType.MergeMesh or AssetType.BlendShapeMesh => ShowMesh,
        AssetType.VFXAttachData or AssetType.VFXATTRData or AssetType.VFXGroupData or AssetType.VFXPrimitiveData => ShowVfx,   // or whatever maps to VFX
        AssetType.SoundBank => ShowAudio,
        _ => ShowData,  // everything else counts as Data
    };

    private bool FilterAsset(object obj)
    {
        if (obj is not AssetEntry asset)
            return false;

        if (!IsTypeVisible(asset))
            return false;

        if (HideEmptyContainers && asset.Size == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return Contains(asset.FileName)
            || Contains(asset.StringAssetID)
            || Contains(asset.Type.ToString());

        bool Contains(string? s) =>
            s?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true;
    }

    partial void OnSearchTextChanged(string value) => _assetsView.Refresh();
    partial void OnShowTexturesChanged(bool value) => AssetsView?.Refresh();
    partial void OnShowMeshChanged(bool value) => AssetsView?.Refresh();
    partial void OnShowVfxChanged(bool value) => AssetsView?.Refresh();
    partial void OnShowAudioChanged(bool value) => AssetsView?.Refresh();
    partial void OnShowDataChanged(bool value) => AssetsView?.Refresh();
    partial void OnHideEmptyContainersChanged(bool value) => AssetsView?.Refresh();


    [RelayCommand]
    private async Task LoadAssetDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Asset Database (AssetDatabase.dat)|AssetDatabase.dat|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Select AssetDatabase.dat"
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ProgressCurrent = 0;
        ProgressTotal = 1;
        StatusText = "Starting load...";

        var progress = new Progress<string>(p =>
        {
            StatusText = p;
        });

        try
        {
            var csvDirectory = Path.GetDirectoryName(dialog.FileName)!;

            // Run the heavy work on a background thread — never block the UI thread
            Library = await Task.Run(() =>
                AssetLibrary.Load(Path.GetDirectoryName(dialog.FileName), progress));

            _previewRegistry.UpdateLibrary(Library);

            var root = await Task.Run(() => FolderNode.BuildTree(Library.All));

            
            RootFolders.Clear();
            RootFolders.Add(new FolderNodeViewModel(root));

            if (DoImportSound)
            {
                
            }

            StatusText = $"Done! Loaded {Library.Count} assets.";
            ProgressTotal = 1;
            ProgressCurrent = 1;

            DebugAssets.Clear();
            SetAssets(Library.All);
            //foreach (var asset in Library.All)
             //   DebugAssets.Add(asset);

        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ShowPreviewAsync(AssetEntry entry)
    {
        var provider = _previewRegistry.GetProvider(entry);
        if (provider is null)
        {
            PreviewContent = null;
            StatusText = $"Cannot preview {entry.Type}";
            ProgressCurrent = 0;
            return;
        }

        try
        {
            PreviewContent = await provider.LoadPreviewAsync(entry);

        }catch (Exception ex)
        {
            PreviewContent = null;
            StatusText = $"Preview failed: {ex.Message}";
        }



    }

    public void AddModfiedSubfile(ModifiedSubAssetEntry modifiedData, AssetEntry parent)
    {
        ModifiedAssetEntry modTarget = null;

        foreach (ModifiedAssetEntry entry in ModifiedAssets)
        {
            if (entry.Parent == parent)
            {
                modTarget = entry;
                break;
            }
        }

        if (modTarget == null)
        {
            modTarget = new ModifiedAssetEntry
            {
                Parent = parent
            };
            ModifiedAssets.Add(modTarget);
        }

        for (int i = 0;  i < modTarget.ModifiedEntries.Count; i++ ) {
            if (modTarget.ModifiedEntries[i].FileName == modifiedData.FileName)
            {
                modTarget.ModifiedEntries[i] = modifiedData;
                return; // Done
            }
        }

        // Otherwise, add a new entry
        modTarget.ModifiedEntries.Add(modifiedData);
    }




    public void SelectFile(AssetEntry entry)
    {
        PreviousEntry = null;
        SelectedEntry = entry;
        _ = ShowPreviewAsync(entry);
    }

    private RelayCommand goBackToPreviousEntry;
    public ICommand GoBackToPreviousEntry => goBackToPreviousEntry ??= new RelayCommand(PerformGoBackToPreviousEntry);

    private void PerformGoBackToPreviousEntry()
    {
        SelectedEntry = PreviousEntry;
        PreviousEntry = null;
        SelectFile(SelectedEntry);
    }

    private RelayCommand exportRaw;
    public ICommand ExportRaw => exportRaw ??= new RelayCommand(PerformExportRaw);

    private void PerformExportRaw()
    {
        if (SelectedEntry == null)
        {
            StatusText = "No file is selected!";
        }
        else
        {
            OpenFolderDialog dialog = new()
            {
                Title = "Select a folder to export too",
            };

            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;
                AssetExtractor.ExtractAll(SelectedEntry, folderPath);
            }


        }
    }

    private RelayCommand subfileViewer_ExportItem;
    public ICommand SubfileViewer_ExportItem => subfileViewer_ExportItem ??= new RelayCommand(PerformSubfileViewer_ExportItem);

    private void PerformSubfileViewer_ExportItem()
    {
        if (SelectedEntry == null || SelectedSubEntry == null)
        {
            StatusText = "No file is selected!";
        }
        else
        {
            var provider = _exporterRegistry.GetProvider(SelectedEntry);
            if (provider == null)
            {
                SaveFileDialog dialog = new()
                {
                    Title = "Select file to export too",
                    FileName = SelectedSubEntry.FileName
                };

                if (dialog.ShowDialog() == true)
                {
                    byte[] data = AssetExtractor.GetSubBlob(SelectedSubEntry, SelectedEntry, SelectedSubEntry.ContentDirectory);

                    var f = File.OpenWrite(dialog.FileName);
                    f.Write(data);
                    f.Close();
                }

            }
            else
            {
                SaveFileDialog dialog = new()
                {
                    Title = "Select file to export too",
                    FileName = System.IO.Path.GetFileNameWithoutExtension(SelectedSubEntry.FileName) + provider.GetExportExtension()
                };

                if (dialog.ShowDialog() == true)
                {
                    bool result = provider.Export(SelectedEntry, dialog.FileName);
                    if (!result)
                    {
                        StatusText = "Couldn't export!";
                    }

                }

            }







        }
    }

    private RelayCommand subfileViewer_ImportItem;
    public ICommand SubfileViewer_ImportItem => subfileViewer_ImportItem ??= new RelayCommand(PerformSubfileViewer_ImportItem);

    private void PerformSubfileViewer_ImportItem()
    {
        if (SelectedEntry == null || SelectedSubEntry == null)
        {
            StatusText = "No file is selected!";
        }
        else
        {
            OpenFileDialog dialog = new()
            {
                Title = "Select a file to import",
                FileName = SelectedSubEntry.FileName
            };

            if (dialog.ShowDialog() == true)
            {
                byte[] data = File.ReadAllBytes(dialog.FileName);
                if (data == null)
                {
                    StatusText = "Error reading file data.";
                    return;
                }

                ModifiedSubAssetEntry modSub = new()
                {
                    FileName = SelectedSubEntry.FileName,
                    ModifiedData = data,
                };

                AddModfiedSubfile(modSub, SelectedEntry);





                /*byte[] data = AssetExtractor.GetSubBlob(SelectedSubEntry, SelectedEntry, SelectedSubEntry.ContentDirectory);

                var f = File.OpenWrite(dialog.FileName);
                f.Write(data);
                f.Close();*/
            }






        }
    }

    private RelayCommand generatePatch;
    public ICommand GeneratePatch => generatePatch ??= new RelayCommand(PerformGeneratePatch);

    private void PerformGeneratePatch()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Select a patch to export too",
            FileName = "@patch_image2.csv",
            Filter= "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RONIN.Modding.PatchGenerator.GeneratePatch(dialog.FileName, ModifiedAssets.ToArray());


        }



    }
}