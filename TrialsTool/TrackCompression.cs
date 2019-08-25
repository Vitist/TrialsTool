using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SevenZip.Compression.LZMA;
using System.Net;

namespace TrialsTool
{
    class TrackCompression
    {
        // A track has 3 parts that are needed to put it back together after editing, the track header, track data and compression properties
        public struct DecompressedTrack
        {
            public byte[] Header;
            public byte[] Data;
            public byte[] Properties;

            public DecompressedTrack(byte[] header, byte[] data, byte[] properties)
            {
                Header = header;
                Data = data;
                Properties = properties;
            }
        }

        // Decompress a track, if decompression fails the struct will contain 0 length byte arrays
        public static DecompressedTrack Decompress(string filePath, Utility.Game game)
        {
            Debug.WriteLine("Decompressing file: " + filePath);
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                // Different versions of the game have different track file format versions with different size headers
                int headerSize;
                switch(game)
                {
                    case Utility.Game.Evolution:
                        headerSize = 37;
                        break;
                    case Utility.Game.Fusion:
                        headerSize = 57;
                        break;
                    case Utility.Game.BloodDragon:
                        headerSize = 57;
                        break;
                    case Utility.Game.Rising:
                        headerSize = 50;
                        break;
                    default:
                        headerSize = 0;
                        break;
                }
                // Header is uncompressed, read it separately
                byte[] header = new byte[headerSize];
                fileStream.Read(header, 0, headerSize);

                // Read the decoder properties
                byte[] properties = new byte[5];
                fileStream.Read(properties, 0, 5);

                // Read in the decompress file size.
                byte[] fileLengthBytes = new byte[8];
                fileStream.Read(fileLengthBytes, 0, 4);
                // Decoder expexts 8 bytes but file only has 4
                for(int i = 4; i < fileLengthBytes.Length; i++)
                {
                    fileLengthBytes[i] = 0x00;
                }
                long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);
                Debug.WriteLine("Uncompressed length: " + fileLength);

                // Setup decoder
                SevenZip.Compression.LZMA.Decoder decoder = new SevenZip.Compression.LZMA.Decoder();
                decoder.SetDecoderProperties(properties);

                // Decompress to memory
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    try
                    {
                        decoder.Code(fileStream, memoryStream, fileStream.Length, fileLength, null);
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine(exception.Message);
                        Debug.WriteLine(exception.ToString());
                        return new DecompressedTrack(new byte[0], new byte[0], new byte[0]);
                    }

                    return new DecompressedTrack(header, memoryStream.ToArray(), properties);
                }
            }
        }

        // Compress track back to a file that the games can read
        public static void Compress(string filePath, DecompressedTrack track)
        {
            // Setup LZMA encoder
            SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();
            SevenZip.CoderPropID[] coderPropIDs = { SevenZip.CoderPropID.DictionarySize, SevenZip.CoderPropID.LitContextBits, SevenZip.CoderPropID.LitPosBits, SevenZip.CoderPropID.PosStateBits};
            object[] properties = LzmaPropertiesFromBytes(track.Properties);
            encoder.SetCoderProperties(coderPropIDs, properties);

            // Write compressed track to file
            using(FileStream fileStream = File.OpenWrite(filePath))
            {
                // Write track header
                fileStream.Write(track.Header, 0, track.Header.Length);

                // Write LZMA header
                encoder.WriteCoderProperties(fileStream);
                fileStream.Write(BitConverter.GetBytes(track.Data.Length), 0, 4);

                // Write compressed track data
                using(MemoryStream memoryStream = new MemoryStream(track.Data))
                {
                    encoder.Code(memoryStream, fileStream, memoryStream.Length, -1, null);
                }
            }
        }

        // Read LZMA compression properties from a byte array
        private static object[] LzmaPropertiesFromBytes(byte[] properties)
        {
            Int32 lc = properties[0] % 9;
            Int32 remainder = properties[0] / 9;
            Int32 lp = remainder % 5;
            Int32 pb = remainder / 5;
            Int32 dictionarySize = 0;
            for (int i = 0; i < 4; i++)
                dictionarySize += ((Int32)(properties[1 + i])) << (i * 8);
            return new object[] { dictionarySize, lc, lp, pb };
        }
    }
}
