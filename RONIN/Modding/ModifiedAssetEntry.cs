using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Modding
{
    public partial class ModifiedAssetEntry : ObservableObject
    {
        [ObservableProperty] private AssetEntry _parent;

        public ObservableCollection<ModifiedSubAssetEntry> ModifiedEntries { get; } = new();

        public string DisplayName => System.IO.Path.GetFileName(Parent?.FileName) ?? "(unknown)";
    }
}
