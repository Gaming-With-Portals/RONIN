using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Importers
{
    public sealed class AssetImporterRegistry
    {
        private readonly List<IAssetImportProvider> _providers = new();

        public void Register(IAssetImportProvider provider) => _providers.Add(provider);

        public IAssetImportProvider? GetProvider(AssetEntry entry) =>
            _providers.FirstOrDefault(p => p.CanImport(entry));
    }
}
