using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using YakumoLib;
using YakumoLib.Assets;

namespace RONIN.Modding
{

    public class PatchGenerator
    {

        static public void GeneratePatch(string csvPath, ModifiedAssetEntry[] modifiedEntries)
        {
            // -- CSV GENERATION --
            StreamWriter csv = new StreamWriter(csvPath);
            FileStream dat = new FileStream(Path.Join(Path.GetDirectoryName(csvPath), Path.GetFileNameWithoutExtension(csvPath) + ".dat"), FileMode.Create, FileAccess.Write);
            csv.WriteLine("header,2,0,"); // HEADER


            long localDataPtrTicker = 0x10;
            long globalDataPtrTicker = 0;

            Dictionary<UUID, long> entryIDToLocalDataOffset = new();
            Dictionary<UUID, long> entryIDToLocalDataSize = new();
            Dictionary<UUID, long> entryIDToTotalDataSize = new();
            Dictionary<UUID, string> entryIDToFileTOC = new(); // absolutely, without-a-doubt the worst way possible to do this
            List<byte[]> binaryTOCs = new();


            // TODO: Asset.bin metadata

            dat.Seek(0x10, 0);

            foreach (ModifiedAssetEntry entry in modifiedEntries) // First pass: Allocate contigous sets of memory for the local files 
            {
                entryIDToLocalDataOffset[entry.Parent.AssetID] = localDataPtrTicker;
                long offsetBase = localDataPtrTicker;
                HashSet<string> modifiedEntryNames = new HashSet<string>(
                    entry.ModifiedEntries.Select(m => m.FileName)
                );

                List<ModifiedSubAssetEntry> modifiedGlobalFiles = new();
                List<SubAssetEntry> originalGlobalFiles = new();

                byte[] tocData = AssetExtractor.GetMetadataToc(entry.Parent);
                localDataPtrTicker += tocData.Length;

                dat.Write(tocData);

                entryIDToFileTOC[entry.Parent.AssetID] = "";

                var orderedSubEntries = entry.Parent.SubEntries; // already correctly ordered



                foreach (SubAssetEntry subentry in orderedSubEntries)
                {
                    if (modifiedEntryNames.Contains(subentry.FileName))
                    {
                        var modified = entry.ModifiedEntries.First(m => m.FileName == subentry.FileName);

                        entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/0/{localDataPtrTicker - offsetBase}/{modified.ModifiedData.Count()}/0/";

                        dat.Seek(localDataPtrTicker, 0);
                        dat.Write(modified.ModifiedData);

                        localDataPtrTicker += (modified.ModifiedData.Length);
                    }
                    else
                    {
                        entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/0/{localDataPtrTicker - offsetBase}/{subentry.Size}/0/";

                        dat.Seek(localDataPtrTicker, 0);
                        dat.Write(AssetExtractor.GetSubBlob(subentry, entry.Parent, subentry.ContentDirectory));

                        localDataPtrTicker += (subentry.Size);

                    }
                }

                /*foreach (ModifiedSubAssetEntry subentry in entry.ModifiedEntries){
                    modifiedEntryNames.Add(subentry.FileName);

                    if (subentry.FileName == "modeldata.mdl" || subentry.FileName.Contains("mip"))
                    {
                        modifiedGlobalFiles.Add(subentry);
                        continue; // global file
                    }

                    entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/0/{localDataPtrTicker-offsetBase}/{subentry.ModifiedData.Count()}/0/";

                    newFilesToRepack.Add(new Tuple<ModifiedSubAssetEntry, long>(subentry, localDataPtrTicker));

                    localDataPtrTicker += (subentry.ModifiedData.Length); // TODO: Align
                }*/

                /*foreach (SubAssetEntry subentry in entry.Parent.SubEntries)
                {
                    if (modifiedEntryNames.Contains(subentry.FileName)) continue;

                    if (subentry.IsGlobal)
                    {
                        originalGlobalFiles.Add(subentry);
                        continue;
                    }

                    entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/0/{localDataPtrTicker-offsetBase}/{subentry.Size}/0/";

                    originalFilesToRepack.Add(new Tuple<SubAssetEntry, ModifiedAssetEntry, long>(subentry, entry, localDataPtrTicker));

                    localDataPtrTicker += (subentry.Size); // TOOD: Align & Compress (maybe)
                }*/

                entryIDToLocalDataSize[entry.Parent.AssetID] = localDataPtrTicker-entryIDToLocalDataOffset[entry.Parent.AssetID];

                /*foreach (ModifiedSubAssetEntry subentry in modifiedGlobalFiles)
                {
                    entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/{offsetBase}/0/{subentry.ModifiedData.Count()}/0/";

                    newFilesToRepack.Add(new Tuple<ModifiedSubAssetEntry, long>(subentry, localDataPtrTicker));

                    localDataPtrTicker += (subentry.ModifiedData.Length);
                }

                foreach (SubAssetEntry subentry in originalGlobalFiles)
                {
                    if (modifiedEntryNames.Contains(subentry.FileName)) continue; // redundant i think

                    entryIDToFileTOC[entry.Parent.AssetID] = entryIDToFileTOC[entry.Parent.AssetID] + $"{subentry.FileName}/{offsetBase}/0/{subentry.Size}/0/";

                    originalFilesToRepack.Add(new Tuple<SubAssetEntry, ModifiedAssetEntry, long>(subentry, entry, localDataPtrTicker));

                    localDataPtrTicker += (subentry.Size); // TOOD: Align & Compress (maybe)
                }*/


                entryIDToTotalDataSize[entry.Parent.AssetID] = localDataPtrTicker - entryIDToLocalDataOffset[entry.Parent.AssetID];

            }

            foreach (ModifiedAssetEntry entry in modifiedEntries) // Third pass: Write CSV data
            {
                byte[] tocData = AssetExtractor.GetMetadataToc(entry.Parent);

                UUID id = entry.Parent.AssetID;

                csv.Write($"Assets/Files/{id.GetString()[0]}/{id.GetString()[1]}/{id.GetString()[2]}/{id.GetString()}/asset.bin,");
                // Offset, Total Size, Size, Compressed Size, File Count
                csv.Write($"{entryIDToLocalDataOffset[entry.Parent.AssetID]},{entryIDToLocalDataSize[entry.Parent.AssetID]},{entryIDToTotalDataSize[entry.Parent.AssetID]},0,{entry.Parent.SubEntries.Count},"); // Offset data

                csv.Write($"36/{entry.Parent.SubEntries.Count}/{tocData.Length-12},"); // bs metadata check later


                csv.Write(entryIDToFileTOC[entry.Parent.AssetID]);

                csv.Write(",\r\n"); // Match CR-LF
            }


            csv.Close();
            dat.Close();
            

            // DONE!

        }


    }
}
