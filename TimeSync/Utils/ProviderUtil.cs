using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using TimeSync.Domain;
using TimeSync.Interfaces;
using TimeSync.Modules;

namespace TimeSync.Utils
{
    public static class ProviderUtil
    {
        private static List<ITimeProvider> _providers;
        public static List<ITimeProvider> GetProviders()
        {
            if (_providers != null)
                return _providers;

            var providerTypes = typeof(SetCredentialsCommand).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ITimeProvider).IsAssignableFrom(t)).ToList();

            var config = ConfigUtil.GetConfig();

            var providers = new List<ITimeProvider>();
            foreach (var providerType in providerTypes)
            {
                var storedCredential = CredentialManager.ReadCredential($"TimeSync_{providerType.Name}");
                var credentials = storedCredential != null
                    ? new Credentials()
                    {
                        Username = storedCredential.UserName,
                        Secret = storedCredential.Password
                    }
                    : null;

                var provider = (ITimeProvider)Activator.CreateInstance(providerType);
                if (provider.AwaitsSetup())
                    continue;

                AnsiConsole.Write("Initializing {0}...", providerType.Name);

                provider.Initialize(credentials, config);

                AnsiConsole.WriteLine();

                providers.Add(provider);
            }

            AnsiConsole.WriteLine();

            _providers = providers;
            return providers;
        }
    }
}
