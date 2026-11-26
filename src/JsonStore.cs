using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace LiveBackground
{
    public static class JsonStore
    {
        // Serializes every write in this process. Saves are rare (a settings
        // apply, a startup save, a build result) so a single lock costs nothing,
        // and it removes the one remaining race: two writers of the same file
        // swapping their temp files into the same destination at once, where the
        // loser's Replace/Move/Copy fails even though the temp names differ.
        private static readonly object WriteSync = new object();

        public static void SerializeFile<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));

            // Write to a temporary file and swap it in, so a crash or power
            // cut halfway through never leaves a half-written config behind.
            // (A truncated file would otherwise load as defaults.) The temp
            // name is unique per call: two writers of the same file (a settings
            // save and a startup save, or a build thread and a manifest write)
            // must never share one temp file, or one would truncate the other's
            // and the "atomic" swap could publish the wrong bytes. The suffix
            // ends in .json.tmp so MediaPipeline's cache sweep can reclaim one
            // left behind by a hard kill.
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".json.tmp";
            lock (WriteSync)
            {
                try
                {
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
                    }
                }
                finally
                {
                    // Replace/Move consume the temp file; a failed write leaves it.
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch { /* best-effort: a stray temp file is harmless and swept later */ }
                }
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
