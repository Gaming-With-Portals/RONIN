using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Formats
{
    public static class DdsTextureLoader
    {
        public static byte[] LoadDDSRaw(AssetEntry entry)
        {
            var imageHeaderEntry = entry.SubEntries?.FirstOrDefault(s => s.FileName.Equals("Image.img"));
            if (imageHeaderEntry is null)
                throw new InvalidDataException("Missing Image.img header.");

            byte[] decompressed = AssetExtractor.GetSubBlob(imageHeaderEntry, entry, imageHeaderEntry.ContentDirectory);
            var br = new BinaryReader(new MemoryStream(decompressed));
            uint totalSize = br.ReadUInt32();
            uint mipMapCount = br.ReadUInt32();

            br.ReadBytes((int)mipMapCount * 8);

            SubAssetEntry[] mipLevels = new SubAssetEntry[mipMapCount];
            for (uint i = 0; i < mipMapCount; i++)
            {
                mipLevels[i] = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals($"mip{i}.img"))
                    ?? throw new InvalidDataException("Missing mip level!");
            }

            byte[] finalTx = br.ReadBytes(148);

            for (uint i = 0; i < mipMapCount; i++)
            {
                finalTx = finalTx.Concat(
                    AssetExtractor.GetSubBlob(mipLevels[i], entry, mipLevels[i].ContentDirectory)
                ).ToArray();
            }

            return finalTx;
        }
    }
}
