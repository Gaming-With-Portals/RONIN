using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public sealed class AssetPreviewRegistry
    {
        private readonly List<IAssetPreviewProvider> _providers = new();

        public void Register(IAssetPreviewProvider provider) => _providers.Add(provider);

        public void UpdateLibrary(AssetLibrary library)
        {
            foreach (var provider in _providers)
            {
                if (provider is IUsesAssetLibrary aware)
                    aware.SetLibrary(library);
            }
        }

        public IAssetPreviewProvider? GetProvider(AssetEntry entry) =>
            _providers.FirstOrDefault(p => p.CanPreview(entry));
    }
}
