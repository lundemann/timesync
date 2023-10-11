using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using TimeSync.Domain;
using TimeSync.Tempo;
using TimeSync.Toolkit;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class CompareCommand : Command<CompareCommand.Settings>
    {
        public class Settings : CommandSettings
        {
            [Description("The full name of the user (must match name in NC Toolkit)")]
            [CommandArgument(0, "[fullName]")]
            public string FullName { get; set; }

            [Description("The Jira user id/guid")]
            [CommandArgument(1, "[jiraUserId]")]
            public string JiraUserId { get; set; }

            [Description("The start date to compare from")]
            [CommandArgument(2, "[from]")]
            public DateTime From { get; set; }

            [Description("The end date to compare to")]
            [CommandArgument(3, "[to]")]
            public DateTime To { get; set; }
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            var providers = ProviderUtil.GetProviders();
            var tempoProvider = providers.First(p => p is TempoTimeProvider);
            var toolkitProvider = providers.First(p => p is ToolkitTimeProvider);

            var registrant = new Registrant
            {
                Name = settings.FullName,
                RegistrantIdentifications = new Dictionary<string, string>
                {
                    { "FullName", settings.FullName },
                    { "AtlassianID", settings.JiraUserId }
                }
            };
            
            var tempoRegistrations = tempoProvider.GetTimeRegistrationEntries(registrant, settings.From, settings.To)
                .GroupBy(e => e.DateExecuted)
                .ToDictionary(g => g.Key, g => g.GroupBy(e => e.AccountIdentifications.TryGetValue("InvoiceAccount", out var ai) ? ai : null));
            var toolkitRegistrations = toolkitProvider.GetTimeRegistrationEntries(registrant, settings.From, settings.To)
                .GroupBy(e => e.DateExecuted)
                .ToDictionary(g => g.Key, g => g.GroupBy(e => e.AccountIdentifications.TryGetValue("InvoiceAccount", out var ai) ? ai : null));

            var table = new Table();
            table.AddColumn("Day");
            table.AddColumn("Account");
            table.AddColumn("Tempo");
            table.AddColumn("Toolkit");
            table.AddColumn("Match");

            for (var d = settings.From; d <= settings.To; d = d.AddDays(1))
            {
                tempoRegistrations.TryGetValue(d, out var tempoGroup);
                toolkitRegistrations.TryGetValue(d, out var toolkitGroup);

                var tempoHours = tempoGroup != null
                    ? tempoGroup.ToDictionary(g => g.Key ?? "", g => g.Sum(e => e.TimeUsed))
                    : new Dictionary<string, double>();
                var toolkitHours = toolkitGroup != null
                    ? toolkitGroup.ToDictionary(g => g.Key ?? "", g => g.Sum(e => e.TimeUsed))
                    : new Dictionary<string, double>();

                var accounts = tempoHours.Keys.Concat(toolkitHours.Keys).Distinct().OrderBy(a => a);

                foreach (var account in accounts)
                {
                    tempoHours.TryGetValue(account, out var tempoAccHours);
                    toolkitHours.TryGetValue(account, out var toolkitAccHours);

                    var tempoHoursString = Math.Abs(tempoAccHours) < Double.Epsilon ? "" : tempoAccHours.ToString("F2");
                    var toolkitHoursString = Math.Abs(toolkitAccHours) < Double.Epsilon ? "" : toolkitAccHours.ToString("F2");
                    var match = tempoHoursString == toolkitHoursString ? "[green]v[/]" : "[red]x[/]";

                    table.AddRow(new Markup(d.ToString("yyyy-MM-dd")),
                        new Markup(account.EscapeMarkup()),
                        new Markup(tempoHoursString),
                        new Markup(toolkitHoursString),
                        new Markup(match));
                }
            }

            AnsiConsole.Write(table);

            return 0;
        }
    }
}
