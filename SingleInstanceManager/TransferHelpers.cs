using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace SingleInstanceManager
{
    internal static class TransferHelpers
    {
        public static string[] ReadArray(this BinaryReader reader)
        {
            int count = reader.ReadInt32();
            string[] array = new string[count];

            for (int i = 0; i < count; i++)
            {
                array[i] = reader.ReadString();
            }

            return array;
        }

        public static IReadOnlyDictionary<string, string> ReadDictionary(this BinaryReader reader)
        {
            int count = reader.ReadInt32();
            Dictionary<string, string> dictionary = new Dictionary<string, string>();

            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                string value = reader.ReadString();
                dictionary.Add(key, value);
            }

            return new ReadOnlyDictionary<string, string>(dictionary);
        }

        public static void WriteArray(this BinaryWriter writer, string[] array)
        {
            writer.Write(array.Length);

            foreach (string item in array)
            {
                writer.Write(item);
            }
        }

        public static void WriteDictionary(this BinaryWriter writer, IDictionary dictionary)
        {
            writer.Write(dictionary.Count);

            foreach (object key in dictionary.Keys)
            {
                writer.Write((string) key);
                writer.Write((string) dictionary[key]);
            }
        }
        
    }
}
