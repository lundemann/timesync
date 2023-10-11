using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using TimeSync.Domain;
using TimeSync.Interfaces;
using TimeSync.Tempo;
using TimeSync.Toolkit;
using TimeSync.Utils;

namespace TimeSync.Modules
{
    public class SyncCommand : Command<SyncCommand.Settings>
    {
        public class Settings : CommandSettings
        {
        }

        private Dictionary<string, string> _accountTitles = new Dictionary<string, string>();

        public override int Execute(CommandContext context, Settings settings)
        {
            var providers = ProviderUtil.GetProviders();
            var tempoProvider = providers.First(p => p is TempoTimeProvider);
            var toolkitProvider = providers.First(p => p is ToolkitTimeProvider);

            var atlId = tempoProvider.GetLoggedInIdentity();
            var tkId = toolkitProvider.GetLoggedInIdentity();

            ((TempoTimeProvider)tempoProvider).ClearAccountIdentificationsCache();

            var registrant = new Registrant
            {
                Name = tkId["FullName"],
                RegistrantIdentifications = new Dictionary<string, string>
                {
                    { "FullName", tkId["FullName"] },
                    { "AtlassianID", atlId["AtlassianID"] }
                }
            };

            var monthCutDate = DateTime.Today.AddDays(-5);
            var nextMonth = DateTime.Today.AddMonths(1);

            var startDate = new DateTime(monthCutDate.Year, monthCutDate.Month, 1);
            var endDate = new DateTime(nextMonth.Year, nextMonth.Month, 1).AddDays(-1);

            var tempoRegistrations = GetTempoRegistrations(tempoProvider, registrant, startDate, endDate);

            var toolkitRegistrations = toolkitProvider.GetTimeRegistrationEntries(registrant, startDate, endDate)
                .GroupBy(e => e.DateExecuted)
                .ToDictionary(g => g.Key, g => g.GroupBy(e => e.AccountIdentifications.TryGetValue("InvoiceAccount", out var ai) ? ai : null));

            var dateDiffs = new Dictionary<DateTime, List<(string Account, double Diff)>>();
            var dateMissingRegistrations = new Dictionary<DateTime, List<(string Account, double Hours)>>();

            for (var d = startDate; d <= endDate; d = d.AddDays(1))
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

                    if (tempoHoursString == toolkitHoursString)
                        continue;

                    if (string.IsNullOrEmpty(toolkitHoursString))
                    {
                        if (!dateMissingRegistrations.TryGetValue(d, out var dayMissingList))
                        {
                            dayMissingList = new List<(string Account, double Hours)>();
                            dateMissingRegistrations[d] = dayMissingList;
                        }

                        dayMissingList.Add((Account: account, tempoAccHours));
                    }
                    else
                    {
                        if (!dateDiffs.TryGetValue(d, out var dayDiffs))
                        {
                            dayDiffs = new List<(string Account, double Diff)>();
                            dateDiffs[d] = dayDiffs;
                        }

                        dayDiffs.Add((Account: account, Diff: toolkitAccHours - tempoAccHours));
                    }
                }
            }

            if (dateDiffs.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]The following differences were found between Tempo and existing Toolkit registrations:[/]");
                string curWeekString = "";
                foreach (var d in dateDiffs.Keys.OrderBy(d => d))
                {
                    var weekString = GetWeekString(d);
                    if (weekString != curWeekString)
                    {
                        AnsiConsole.WriteLine(weekString + ":");
                        curWeekString = weekString;
                    }

                    foreach (var diff in dateDiffs[d].OrderBy(da => da.Account))
                    {
                        AnsiConsole.MarkupLine("{0:yyyy-MM-dd}, account {1}: [yellow]{2:F2}[/]", d, GetAccountTitle(diff.Account), diff.Diff);
                    }
                }

                AnsiConsole.WriteLine();
                var overwrite = AnsiConsole.Ask($"Do you want to continue with transferring other hours despite differing existing registrations y/n?", "n");
                if (overwrite?.ToLowerInvariant() != "y")
                    return 0;
            }

            if (dateMissingRegistrations.Count != 0)
            {
                var prompt = new MultiSelectionPrompt<(string Week, DateTime? Day, string Account, double Hours)>()
                    .Title("Choose the hours to sync to Toolkit")
                    .PageSize(20)
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle entries, [green]<enter>[/] to accept)[/]");
                prompt.Converter = Format;

                var regsByWeek = dateMissingRegistrations.GroupBy(kvp => GetWeekString(kvp.Key))
                    .OrderBy(g => g.First().Key);

                foreach (var weekRegs in regsByWeek)
                {
                    var weekHours = weekRegs.Sum(d => d.Value.Sum(ah => ah.Hours));
                    var weekItem = prompt.AddChoice((Week: weekRegs.Key, Day: null, Account: null, Hours: weekHours));

                    foreach (var dayRegs in weekRegs.OrderBy(d => d.Key))
                    {
                        var dayHours = dayRegs.Value.Sum(ah => ah.Hours);
                        var dayItem = weekItem.AddChild((Week: null, Day: dayRegs.Key, Account: null, Hours: dayHours));

                        foreach (var accountReg in dayRegs.Value.OrderBy(ah => ah.Account))
                        {
                            dayItem.AddChild((Week: null, Day: dayRegs.Key, Account: accountReg.Account, accountReg.Hours));
                        }
                    }
                }

                var entriesToSync = AnsiConsole.Prompt(prompt);

                foreach (var entryToSync in entriesToSync)
                {
                    toolkitProvider.CreateTimeRegistrationEntry(new TimeRegistrationEntry
                    {
                        DateExecuted = entryToSync.Day.Value,
                        TimeUsed = entryToSync.Hours,
                        Registrant = registrant,
                        AccountIdentifications = new Dictionary<string, string>
                        {
                            { "InvoiceAccount", entryToSync.Account },
                            { "InvoiceAccountText", GetAccountTitle(entryToSync.Account) }
                        }
                    });
                }

                AnsiConsole.WriteLine("All chosen time registration entries were transferred");

                return 0;
            }
            else
            {
                AnsiConsole.WriteLine("No additional time registration entries to transfer");
                return 0;
            }
        }

        private string GetAccountTitle(string account)
        {
            return _accountTitles.TryGetValue(account, out var title) ? title : account;
        }

        private Dictionary<DateTime, IEnumerable<IGrouping<string, TimeRegistrationEntry>>> GetTempoRegistrations(ITimeProvider tempoProvider, Registrant registrant, DateTime startDate,
            DateTime endDate)
        {
            var tempoRegistrations = tempoProvider.GetTimeRegistrationEntries(registrant, startDate, endDate)
                .GroupBy(e => e.DateExecuted)
                .ToDictionary(g => g.Key,
                    g => g.GroupBy(e => e.AccountIdentifications.TryGetValue("InvoiceAccount", out var ai) ? ai : null));

            _accountTitles = new Dictionary<string, string>();
            var missingAccountErrors = new List<string>();
            foreach (var dayGroup in tempoRegistrations)
            {
                foreach (var accountGroup in dayGroup.Value)
                {
                    if (accountGroup.Key == null)
                        missingAccountErrors.Add(
                            $"Missing/closed account on issue {accountGroup.First().AccountIdentifications["JiraIssueKey"]}");
                    else
                        _accountTitles[accountGroup.Key] = accountGroup.First().AccountIdentifications["InvoiceAccountText"];
                }
            }

            if (missingAccountErrors.Count > 0)
            {
                AnsiConsole.WriteLine(string.Join(Environment.NewLine, missingAccountErrors));
                var clearCacheAndRetry = AnsiConsole.Ask("Clear cache and try again y/n?", "n");
                if (clearCacheAndRetry?.ToLowerInvariant() == "y")
                {
                    ((TempoTimeProvider)tempoProvider).ClearAccountIdentificationsCache();
                    return GetTempoRegistrations(tempoProvider, registrant, startDate, endDate);
                }

                throw new InvalidOperationException("Cannot transfer time registration entries without an account.");
            }

            return tempoRegistrations;
        }

        private string Format((string Week, DateTime? Day, string Account, double Hours) value)
        {
            if (value.Week != null)
                return $"{value.Week}: {value.Hours:F2}h";
            if (value.Account == null)
                return $"{value.Day:d}: {value.Hours:F2}h";

            return $"{GetAccountTitle(value.Account)}: {value.Hours:F2}h";
        }

        private string GetWeekString(DateTime date)
        {
            var week = DateUtil.GetWeekNumber(date);
            return $"{week.Year}, week {week.Week}";
        }
    }
}
