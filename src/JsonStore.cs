using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace LiveBackground
{
    public static class JsonStore
    {
        public static void SerializeFile<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));

            // Write to a temporary file and swap it in, so a crash or power
            // cut halfway through never leaves a half-written config behind.
            // (A truncated file would otherwise load as defaults.)
            string temp = path + ".tmp";
            using (FileStream fs = File.Create(temp))
            using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true))
            {
                serializer.WriteObject(writer, value);
                writer.Flush();
            }
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch
            {
                // Fall back to a plain copy when a Replace is not possible,
                // for example on some network shares.
                File.Copy(temp, path, true);
                try { File.Delete(temp); } catch { }
            }
        }

        public static T DeserializeFile<T>(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                // A UTF-8 BOM (EF BB BF) breaks the JSON reader with a
                // confusing "unexpected character" error, but editors and
                // scripts happily add one. Skip it when present.
                long position = fs.Position;
                if (fs.Length >= 3)
                {
                    int b1 = fs.ReadByte();
                    int b2 = fs.ReadByte();
                    int b3 = fs.ReadByte();
                    if (b1 == 0xEF && b2 == 0xBB && b3 == 0xBF)
                    {
                        // BOM skipped; the stream now starts at the JSON.
                    }
                    else
                    {
                        fs.Seek(position, SeekOrigin.Begin);
                    }
                }
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(fs);
            }
        }

        public static T Deserialize<T>(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(ms);
            }
        }
    }
}
