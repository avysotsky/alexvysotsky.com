using static CoreDTO.Logger.Enums;
using System.Collections.Generic;

namespace CoreDTO.Args
{
    public class Parser
    {
        public static Dictionary<string, string> ParseArgs(string[] args, LogAction Debug)
        {
            var result = new Dictionary<string, string>();
            foreach (var param in args)
            {
                var keyValue = param.Split('=');
                if (keyValue.Length == 2)
                {
                    result.Add(keyValue[0], keyValue[1]);
                }
                else
                {
                    Debug($"Invalid parametr - {param}. Expected 'key=value' format", LogLevel.llInit);
                }
            }
            return result;
        }
    }
}
