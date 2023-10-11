using Spectre.Console.Cli;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class ClearCacheCommand : Command<ClearCacheCommand.Settings>
    {
        public class Settings : CommandSettings
        {
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            CacheUtil.Clear();

            return 0;
        }
    }
}
