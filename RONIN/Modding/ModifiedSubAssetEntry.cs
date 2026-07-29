using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RONIN.Modding
{
    public sealed record ModifiedSubAssetEntry
    {
        public required string FileName { get; init; }

        public required byte[] ModifiedData { get; init; }


        public string Extension => System.IO.Path.GetExtension(FileName);

    }
}
