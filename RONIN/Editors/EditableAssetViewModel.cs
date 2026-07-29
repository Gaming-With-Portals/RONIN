using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Editors
{

    // Friendly abstraction to help with making simple GUI editors.
    public abstract partial class EditableAssetViewModel : ObservableObject, IEditableAsset
    {
        public AssetEntry ParentEntry { get; }
        public string SubFileName { get; }

        public event EventHandler? Modified;

        protected EditableAssetViewModel(AssetEntry parent, string subFileName)
        {
            ParentEntry = parent;
            SubFileName = subFileName;
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            Modified?.Invoke(this, EventArgs.Empty);
        }

        public abstract byte[] Serialize();

    }
}
