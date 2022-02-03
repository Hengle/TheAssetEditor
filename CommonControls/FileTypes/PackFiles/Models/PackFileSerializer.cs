﻿using CommonControls.Common;
using FileTypes.ByteParsing;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CommonControls.FileTypes.PackFiles.Models
{

    public static class PackFileVersionConverter
    {
        static List<(PackFileVersion EnumValue, string StringValue)> _values = new List<(PackFileVersion EnumValue, string StringValue)>()
        {
            (PackFileVersion.PFH0,  "PFH0"),
            (PackFileVersion.PFH2,  "PFH2"),
            (PackFileVersion.PFH3,  "PFH3"),
            (PackFileVersion.PFH4,  "PFH4"),
            (PackFileVersion.PFH5,  "PFH5"),
            (PackFileVersion.PFH6,  "PFH6"),
        };

        public static string ToString(PackFileVersion versionEnum) => _values.First(x => x.EnumValue == versionEnum).StringValue; 
        public static PackFileVersion GetEnum(string versionStr) => _values.First(x => x.StringValue == versionStr.ToUpper()).EnumValue;
    }

    public static class PackFileSerializer
    {
        static readonly ILogger _logger = Logging.CreateStatic(typeof(PackFileSerializer));

        public static PackFileContainer Load(string packFileSystemPath, BinaryReader reader, IAnimationFileDiscovered animationFileDiscovered)
        {
            try
            {
                var fileNameBuffer = new byte[1024];
                var name = Path.GetFileNameWithoutExtension(packFileSystemPath);
                var output = new PackFileContainer(name)
                {
                    SystemFilePath = packFileSystemPath,
                    Name = Path.GetFileNameWithoutExtension(packFileSystemPath),
                    Header = ReadHeader(reader),
                    OriginalLoadByteSize = new FileInfo(packFileSystemPath).Length,
                };


                // If larger then int.max throw error
                if (output.Header.FileCount > int.MaxValue)
                    throw new Exception("To many files in packfile!");

                output.FileList = new Dictionary<string, PackFile>((int)output.Header.FileCount);

                PackedFileSourceParent packedFileSourceParent = new PackedFileSourceParent()
                {
                    FilePath = packFileSystemPath,
                };

                long offset = output.Header.DataStart; 
                for (int i = 0; i < output.Header.FileCount; i++)
                {
                    uint size = reader.ReadUInt32();

                    if (output.Header.HasIndexWithTimeStamp)
                        reader.ReadUInt32();

                    byte isCompressed = 0;
                    if (output.Header.Version == PackFileVersion.PFH5)
                        isCompressed = reader.ReadByte();   // Is the file actually compressed, or is it just a compressed format?

                    string fullPackedFileName = IOFunctions.ReadZeroTerminatedAscii(reader, fileNameBuffer).ToLower();

                    var packFileName = Path.GetFileName(fullPackedFileName);
                    var fileContent = new PackFile(packFileName, new PackedFileSource(packedFileSourceParent, offset, size));

                    if (animationFileDiscovered != null && packFileName.EndsWith(".anim"))
                        animationFileDiscovered.FileDiscovered(fileContent, output, fullPackedFileName);

                    output.FileList.Add(fullPackedFileName, fileContent);
                    offset += size;
                }

                return output;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Failed to load packfile {packFileSystemPath} - {e.Message}");
                throw;
            }
        }

        static PFHeader ReadHeader(BinaryReader reader)
        {
            var fileNameBuffer = new byte[1024];
            var header = new PFHeader()
            {
                _strVersion = new string(reader.ReadChars(4)),      // 4
                ByteMask = reader.ReadInt32(),                  // 8
                ReferenceFileCount = reader.ReadUInt32(),       // 12    
            };

            header.Version = PackFileVersionConverter.GetEnum(header._strVersion);

            var pack_file_index_size = reader.ReadUInt32();         // 16
            var pack_file_count = reader.ReadUInt32();              // 20
            var packed_file_index_size = reader.ReadUInt32();       // 24

            // Read the buffer of data stuff
            if (header.Version == PackFileVersion.PFH0)
            {
                header.Buffer = new byte[0];
            }
            else if (header.Version == PackFileVersion.PFH2 || header.Version == PackFileVersion.PFH3)
            {
                header.Buffer = reader.ReadBytes(8);

                // Uint64 timestamp
            }
            else if (header.Version == PackFileVersion.PFH4 || header.Version == PackFileVersion.PFH5)
            {
                if (header.HasExtendedHeader)
                    header.Buffer = reader.ReadBytes(24);
                else
                    header.Buffer = reader.ReadBytes(4);

                // Uint32 timestamp
                // output.HasExtendedHeader 20 bytes missing? Used by Arena, we dont care 
            }
            else if (header.Version == PackFileVersion.PFH6)
            {
                header.Buffer = reader.ReadBytes(284);

                // game_version u32
                // build_number u32
                // authoring_tool char 44
                // extra_subheader_data u32, not used 
            }
            else
            {
                throw new Exception($"Unknown packfile type {header.PackFileType}");
            }

            for (int i = 0; i < header.ReferenceFileCount; i++)
                header.DependantFiles.Add(IOFunctions.ReadZeroTerminatedAscii(reader, fileNameBuffer));
            
            header.DataStart = 24 + (uint)header.Buffer.Length + pack_file_index_size + packed_file_index_size;
            header.FileCount = pack_file_count;

            return header;
        }

    }
}
