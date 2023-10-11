using Spectre.Console.Cli;
using TimeSync.TogglTrack;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class SetupTogglCommand : Command<SetupTogglCommand.Settings>
    {
        public class Settings : CommandSettings
        {
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            var togglProvider = new TogglTrackTimeProvider();
            togglProvider.Initialize(null, ConfigUtil.GetConfig());

            SetCredentialsCommand.SetupProviderCredentials(togglProvider);

            return 0;
        }
    }
}
