using System.Collections.Generic;
using System.Configuration;

namespace TimeSync.Utils
{
    public static class ConfigUtil
    {
        public static Dictionary<string, string> GetConfig()
        {
            var config = new Dictionary<string, string>();
            config["JiraUrl"] = ConfigurationManager.AppSettings["JiraUrl"];
            config["TogglApiUrl"] = ConfigurationManager.AppSettings["TogglApiUrl"];

            return config;
        }

        public static T GetConfigSection<T>(string sectionName)
        {
            return (T)ConfigurationManager.GetSection(sectionName);
        }
    }
}
