using System;
using System.Collections.Generic;

namespace CoreDTO.Tools
{
    public static class EmailGenerator
    {
        public static string GenerateEmailFromGUID(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                domain = "lead.bot";
            }
            Guid guid = Guid.NewGuid();
            string emailPrefix = guid.ToString("N").Substring(0, 25);
            return $"{emailPrefix}@{domain}";
        }
        public static string GenerateEmail(string name, string phone)
        {
            try
            {
                name = name.Replace(" ", "_");
                string email = $"{Transliterate(name)}{phone.Substring(phone.Length - 4)}@lead.bot";
                return email;

            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        static string Transliterate(string input)
        {
            Dictionary<char, string> translitTable = new Dictionary<char, string>()
            {
                {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"}, {'е', "e"}, {'ё', "yo"}, {'ж', "zh"},
                {'з', "z"}, {'и', "i"}, {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"}, {'о', "o"},
                {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"}, {'у', "u"}, {'ф', "f"}, {'х', "h"}, {'ц', "ts"},
                {'ч', "ch"}, {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""}, {'э', "e"}, {'ю', "yu"},
                {'я', "ya"}
            };

            string result = string.Empty;

            foreach (char c in input.ToLower())
            {
                if (translitTable.ContainsKey(c))
                {
                    result += translitTable[c];
                }
                else
                {
                    result += c;
                }
            }

            return result;
        }
    }
}
