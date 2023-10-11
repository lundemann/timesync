using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using TimeSync.Domain;
using TimeSync.Tempo;
using TimeSync.TogglTrack;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class TransferFromTogglCommand : Command<TransferFromTogglCommand.Settings>
    {
        public class Settings : CommandSettings
        {
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            var providers = ProviderUtil.GetProviders();
            var togglProvider = providers.First(p => p is TogglTrackTimeProvider);
            var tempoProvider = providers.First(p => p is TempoTimeProvider);

            var togId = togglProvider.GetLoggedInIdentity();
            var atlId = tempoProvider.GetLoggedInIdentity();

            var registrant = new Registrant
            {
                RegistrantIdentifications = new Dictionary<string, string>
                {
                    { "TogglWorkspace", togId["TogglWorkspace"] },
                    { "AtlassianID", atlId["AtlassianID"] }
                }
            };

            var lastTransfer = (DateTime?)CacheUtil.Get<object>("LastTogglTransferDate");
            var from = lastTransfer != null ? lastTransfer.Value.AddDays(1) : DateTime.Today.AddDays(-4);
            if (from.CompareTo(DateTime.Now) > 0)
                return 0;

            var entries = togglProvider.GetTimeRegistrationEntries(registrant, from, DateTime.Today);

            if (entries.Count == 0)
                return 0;

            var regDate = lastTransfer != null ? entries.Min(e => e.DateExecuted) : entries.Max(e => e.DateExecuted);
            regDate = new DateTime(regDate.Year, regDate.Month, regDate.Day, 0, 0, 0, DateTimeKind.Local);
            var regDateEnd = regDate.AddDays(1);

            var lastDayRegs = entries.Where(e => e.DateExecuted.CompareTo(regDate) >= 0 && e.DateExecuted.CompareTo(regDateEnd) < 0).ToArray();

            int quarterFloorSum = 0;
            double quarterSum = 0d;
            var quarterSums = new Dictionary<string, (double QuarterSum, double Fraction)>();
            var issueDescs = new Dictionary<string, string>();
            foreach (var issueTimes in lastDayRegs.GroupBy(e => e.AccountIdentifications.TryGetValue("JiraIssueKey", out var issue) ? issue : ""))
            {
                double issueQuarterSum = issueTimes.Sum(e => e.TimeUsed) * 4d;
                double issueQuarterSumFloor = Math.Floor(issueQuarterSum);
                quarterSum += issueQuarterSum;
                quarterFloorSum += (int)issueQuarterSumFloor;
                quarterSums[issueTimes.Key] = (QuarterSum: issueQuarterSum, Fraction: issueQuarterSum - issueQuarterSumFloor);

                issueDescs[issueTimes.Key] = string.Join("; ", issueTimes.GroupBy(t => t.AccountIdentifications["TogglDescription"])
                    .Select(t => t.Key));
            }

            int bestQuarterSum = (int)Math.Round(quarterSum, MidpointRounding.ToEven);
            int toCeil = bestQuarterSum - quarterFloorSum;
            var issueRoundedTimes = new Dictionary<string, double>();
            foreach (var issueTime in quarterSums.OrderByDescending(x => x.Value.Fraction))
            {
                double roundedTime = toCeil > 0 ? Math.Ceiling(issueTime.Value.QuarterSum) : Math.Floor(issueTime.Value.QuarterSum);
                issueRoundedTimes[issueTime.Key] = roundedTime / 4;
                --toCeil;
            }

            AnsiConsole.MarkupLine("[yellow]Did you remember to stop the timer?[/]");

            while (true)
            {
                double dayHours = 0;
                List<(string Title, string Id)> choices = new List<(string Title, string Id)>();
                choices.Add((Title: "No edits", Id: "{NO_EDITS}"));
                choices.Add((Title: "Don't transfer entries from Toggl", Id: "{NO_TRANSFER}"));
                foreach (var issue in issueRoundedTimes.Keys.OrderBy(i => i))
                {
                    dayHours += issueRoundedTimes[issue];
                    choices.Add((Title: $"{issueDescs[issue]}: {issueRoundedTimes[issue]:F2}h", Id: issue));
                }

                var prompt = new SelectionPrompt<(string Title, string Id)>()
                    .Title($"Edit entries [green]{dayHours:F2}h[/]?")
                    .UseConverter(t => t.Title)
                    .AddChoices(choices);
                var chosenDefault = AnsiConsole.Prompt(prompt);

                if (chosenDefault.Id == "{NO_TRANSFER}")
                    return 0;

                if (chosenDefault.Id == "{NO_EDITS}")
                    break;

                var newTime = AnsiConsole.Ask($"New value for {issueDescs[chosenDefault.Id]}: ", issueRoundedTimes[chosenDefault.Id]);
                issueRoundedTimes[chosenDefault.Id] = newTime;
            }

            foreach (var issue in issueRoundedTimes.Keys.OrderBy(i => i))
            {
                AnsiConsole.WriteLine("{0}: {1:F2}", issue, issueRoundedTimes[issue]);
            }

            foreach (var issueKey in issueRoundedTimes.Keys)
            {
                if (string.IsNullOrEmpty(issueKey) || (-double.Epsilon < issueRoundedTimes[issueKey] && issueRoundedTimes[issueKey] < double.Epsilon))
                    continue;

                tempoProvider.CreateTimeRegistrationEntry(new TimeRegistrationEntry
                {
                    Registrant = registrant,
                    AccountIdentifications = new Dictionary<string, string>
                    {
                        { "JiraIssueKey", issueKey }
                    },
                    TimeUsed = issueRoundedTimes[issueKey],
                    DateExecuted = regDate
                });
            }

            CacheUtil.Set("LastTogglTransferDate", (object)regDate);

            return 0;
        }
    }
}
