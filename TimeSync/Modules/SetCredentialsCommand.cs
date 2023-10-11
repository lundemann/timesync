using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TimeSync.Interfaces;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class SetCredentialsCommand : Command<SetCredentialsCommand.Settings>
    {
        public class Settings : CommandSettings
        {
            [CommandOption("--only-unauthorized")]
            [DefaultValue(false)]
            public bool OnlyUnauthorized { get; set; }
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            var providers = ProviderUtil.GetProviders();

            foreach (var provider in providers)
            {
                var providerType = provider.GetType();

                if (provider.IsAuthenticated())
                {
                    if (settings.OnlyUnauthorized)
                        continue;

                    var overwrite = AnsiConsole.Ask($"{providerType.Name} already has configured credentials. Overwrite y/n?", "n");
                    if (overwrite?.ToLowerInvariant() != "y")
                        continue;
                }

                SetupProviderCredentials(provider);
            }

            return 0;
        }

        internal static void SetupProviderCredentials(ITimeProvider provider)
        {
            var providerType = provider.GetType();

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine($"Input credentials for {providerType.Name}");
            var newCredentials = provider.AskUserForCredentials();

            if (newCredentials == null)
            {
                AnsiConsole.MarkupLine("[red]Authentication failed[/]");
                return;
            }

            CredentialManager.WriteCredential($"TimeSync_{providerType.Name}", newCredentials.Username,
                newCredentials.Secret, CredentialManager.CredentialPersistence.LocalMachine);
        }
    }
}
