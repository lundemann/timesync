using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace TimeSync.Utils
{
    public static class CacheUtil
    {
        private static JObject _cacheRoot;

        public static T Get<T>(string key) where T : class
        {
            EnsureRead();

            return _cacheRoot[key]?.ToObject<T>();
        }

        public static void Set<T>(string key, T value) where T : class
        {
            EnsureRead();

            _cacheRoot[key] = value != null ? JToken.FromObject(value) : null;

            var jsonData = _cacheRoot.ToString();
            File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeSync", "Cache.json"), jsonData);
        }

        public static void Clear()
        {
            _cacheRoot = new JObject();
            var jsonData = _cacheRoot.ToString();
            File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeSync", "Cache.json"), jsonData);
        }

        private static void EnsureRead()
        {
            if (_cacheRoot != null)
                return;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cacheDir = Path.Combine(appData, "TimeSync");
            Directory.CreateDirectory(cacheDir);

            var cacheFileName = Path.Combine(cacheDir, "Cache.json");
            if (!File.Exists(cacheFileName))
                File.WriteAllText(cacheFileName, @"{}");

            var jsonData = File.ReadAllText(cacheFileName);
            _cacheRoot = JObject.Parse(jsonData);
        }
    }
}
