using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Sudoku.Shared.Utils
{
    public static class JsonHelper
    {
        public static string Serialize<T>(T value)
        {
            if (value == null) return null;
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("JSON payload is empty.");
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (T)serializer.ReadObject(stream);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("JSON payload is invalid.", exception);
            }
        }
    }
}
