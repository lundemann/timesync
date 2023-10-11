using System.ComponentModel;
using Spectre.Console.Cli;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class SetDefaultValuesCommand : Command<SetDefaultValuesCommand.Settings>
    {
        public class Settings : CommandSettings
        {
            [CommandOption("--only-missing")]
            [DefaultValue(false)]
            public bool OnlyMissing { get; set; }
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            var providers = ProviderUtil.GetProviders();

            foreach (var provider in providers)
            {
                provider.PromptDefaultValues(settings.OnlyMissing);
            }

            return 0;
        }
    }
}
