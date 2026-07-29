using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Exporters
{
    public sealed class AssetExporterRegistry
    {
        private readonly List<IAssetExporterProvider> _providers = new();

        public void Register(IAssetExporterProvider provider) => _providers.Add(provider);

        public IAssetExporterProvider? GetProvider(AssetEntry entry) =>
            _providers.FirstOrDefault(p => p.CanExport(entry));
    }
}
