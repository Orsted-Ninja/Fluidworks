using System;
using System.Collections.Generic;

namespace FluidWorks.Reporting
{
    public static class TemplateParser
    {
        public static string ReplacePlaceholders(string template, Dictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(template)) return "";
            
            string result = template;
            foreach (var kvp in data)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }

        public static string ReplacePlaceholder(string template, string key, string value)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return template.Replace(key, value);
        }
    }
}
