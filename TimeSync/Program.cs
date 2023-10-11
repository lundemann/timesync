using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using TimeSync.Interfaces;
using TimeSync.Modules;
using TimeSync.TogglTrack;
using TimeSync.Utils;

namespace TimeSync
{
    static class Program
    {
        internal static bool Interactive { get; set; }
        static int Main(string[] args)
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                config.AddCommand<SetCredentialsCommand>("set-credentials")
                    .WithAlias("setcred");
                config.AddCommand<CompareCommand>("compare");
                config.AddCommand<SyncCommand>("sync");
                config.AddCommand<ClearCacheCommand>("clear-cache")
                    .WithAlias("clearcache");
                config.AddCommand<SetDefaultValuesCommand>("set-default-values")
                    .WithAlias("setdefaults");
                config.AddCommand<SetupTogglCommand>("setup-toggl");
                config.AddCommand<TransferFromTogglCommand>("transfer-from-toggl");
            });

            if (args.Length == 0)
            {
                Interactive = true;
                List<ITimeProvider> providers;
                try
                {
                    providers = ProviderUtil.GetProviders();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
                    ExitPrint();
                    return -1;
                }

                if (providers.Any(p => !p.IsAuthenticated()))
                {
                    app.Run(new[] { "set-credentials", "--only-unauthorized" });
                }

                app.Run(new[] { "set-default-values", "--only-missing" });

                if (providers.Any(p => p is TogglTrackTimeProvider))
                {
                    app.Run(new[] { "transfer-from-toggl" });
                }

                args = new[] { "sync" };
            }

            var result = app.Run(args);

            ExitPrint();
            return result;
        }

        private static void ExitPrint()
        {
            if (Interactive)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine("Press ENTER to exit the program");
                Console.ReadLine();
            }
        }
    }
}
