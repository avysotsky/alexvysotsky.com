using static CoreDTO.Logger.Enums;
using CoreDTO.TextConst;
using System;
using System.IO;
using System.Text.Json;

namespace CoreDTO.Configs
{
    public class JsonConfig<T> where T : class
    {

        public static T? DeserializeFromJsonString(string ut8jsonString, LogAction? logAction) 
        {
            try
            {
                return JsonSerializer.Deserialize<T>(ut8jsonString);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"[{LogHashTags.externalServiceError}] {ut8jsonString} deserialization errror - {ex}", LogLevel.llExceptions);
                return null;
            }
        }

        public static Ancessor? DeserializeFromJsonString<Ancessor>(string ut8jsonString, LogAction? logAction) where Ancessor : class
        {
            MemoryStream mem = new MemoryStream();
            try
            {
                return JsonSerializer.Deserialize<Ancessor>(ut8jsonString, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"[{LogHashTags.externalServiceError}] {ut8jsonString} deserialization errror - {ex}", LogLevel.llExceptions);
                return null;
            }
        }
        public static T DeserializeFromJsonFromFile(string fileName, LogAction? logAction)
        {
            FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            try
            {
                return JsonSerializer.Deserialize<T>(stream);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"[{LogHashTags.configurationErrror}] {fileName} deserialization errror - {ex}", LogLevel.llExceptions);
                return null;
            }
        }

        public static void Serialize(string fileName, T value, LogAction? logAction)
        {
            try
            {
                using FileStream stream = new FileStream(fileName, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize<T>(stream, value);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"[{LogHashTags.configurationErrror}] {ex}", LogLevel.llExceptions);
            }
        }
    }
}
