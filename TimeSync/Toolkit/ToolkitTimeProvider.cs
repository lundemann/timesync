using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.SharePoint.Client;
using Spectre.Console;
using TimeSync.Domain;
using TimeSync.Interfaces;
using TimeSync.Utils;

namespace TimeSync.Toolkit
{
    public class ToolkitTimeProvider : ITimeProvider
    {
        private bool _initialized = false;
        private ToolkitSettings _settings;
        private ClientContext _clientContext;
        private CookieContainer _clientContextCookies;
        private List _list;

        public void Initialize(Credentials credentials, Dictionary<string, string> config)
        {
            _settings = ConfigUtil.GetConfigSection<ToolkitSettings>("toolkitSettings");

            //_clientContextCookies = GetLoginCookie(credentials);
            _clientContextCookies = GetCookieContainer();

            _clientContext = new ClientContext(_settings.Url);
            _clientContext.ExecutingWebRequest += (sender, e) =>
            {
                if (_clientContextCookies != null)
                    e.WebRequestExecutor.WebRequest.CookieContainer = _clientContextCookies;
            };

            InitializeList();

            _initialized = true;
        }

        private CookieContainer GetCookieContainer()
        {
            try
            {
                return BrowserLogin.GetLoginCookies(_settings.Url);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                AnsiConsole.WriteLine();

                var clearCacheAndRetry = AnsiConsole.Ask("Browser based login failed. Clear login information and try again y/n?", "n");
                if (clearCacheAndRetry?.ToLowerInvariant() == "y")
                {
                    Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TimeSync\WebView2UserData"), true);
                    return GetCookieContainer();
                }

                throw;
            }
        }

        private CookieContainer GetLoginCookie(Credentials credentials)
        {
            var authCookiesContainer = new CookieContainer();
            NetworkCredential netCreds = null;
            if (credentials != null)
            {
                netCreds = new NetworkCredential(credentials.Username, credentials.Secret);
            }

            using (var handler = new HttpClientHandler { Credentials = netCreds })
            using (var client = new HttpClient(handler))
            {
                Uri referrer;
                Uri kitUrl = new Uri(_settings.Url);
                Uri newUrl;
                client.BaseAddress = new Uri(kitUrl.GetLeftPart(UriPartial.Authority));
                AddDefaultHeaders(client);
                using (var req = new HttpRequestMessage(HttpMethod.Get, kitUrl.PathAndQuery))
                using (var resp = client.SendAsync(req).Result)
                {
                    newUrl = resp.RequestMessage.RequestUri;
                    referrer = resp.RequestMessage.RequestUri;
                }

                using (var req = new HttpRequestMessage(HttpMethod.Get, "/_windows/default.aspx" + newUrl.Query))
                {
                    req.Headers.Referrer = referrer;
                    using (var resp = client.SendAsync(req).Result)
                    {
                        if (handler.CookieContainer.GetCookies(kitUrl)["FedAuth"] == null)
                        {
                            _list = null;
                            _initialized = true;
                            return null;
                        }

                        authCookiesContainer = handler.CookieContainer;
                    }
                }
            }

            return authCookiesContainer;
        }

        private static void AddDefaultHeaders(HttpClient client)
        {
            foreach (var header in _defaultHeaders.Split(new [] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var headerParts = header.Split(new [] { ':' }, 2);
                client.DefaultRequestHeaders.Add(headerParts[0], headerParts[1].TrimStart());
            }
        }

        private static string _defaultHeaders = @"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7
Accept-Encoding: gzip, deflate, br
Accept-Language: da
Connection: keep-alive
Sec-Fetch-Dest: document
Sec-Fetch-Mode: navigate
Sec-Fetch-Site: same-origin
Sec-Fetch-User: ?1
Upgrade-Insecure-Requests: 1
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36 Edg/114.0.1823.51
sec-ch-ua: ""Not.A/Brand"";v=""8"", ""Chromium"";v=""114"", ""Microsoft Edge"";v=""114""
sec-ch-ua-mobile: ?0
sec-ch-ua-platform: ""Windows""";

        private void InitializeList()
        {
            if (_clientContextCookies == null)
            {
                _list = null;
                return;
            }

            _list = _clientContext.Web.Lists.GetByTitle(_settings.TimeRegistrationList);
            _clientContext.Load(_list);
            try
            {
                _clientContext.ExecuteQuery();
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null || resp.StatusCode != HttpStatusCode.Unauthorized)
                    throw;

                _list = null;
            }
        }

        public Credentials AskUserForCredentials()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");
            
            //Console.Write("Username (domain\\username): ");
            //var username = Console.ReadLine();

            //Console.Write("Password: ");
            //var password = InputUtil.ReadMaskedInput();
            //Console.WriteLine();

            //var newCreds = new Credentials
            //{
            //    Username = username,
            //    Secret = password
            //};
            _clientContextCookies = GetCookieContainer();
            //_clientContextCookies = GetLoginCookie(newCreds);

            //_clientContext.Credentials = new NetworkCredential(username, password);

            InitializeList();
            if (!IsAuthenticated())
                return null;

            return null;
            //return new Credentials
            //{
            //    Username = username,
            //    Secret = password
            //};
        }

        public Dictionary<string, string> GetLoggedInIdentity()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to calling this method");

            var currentUser = GetCurrentUser();

            return new Dictionary<string, string>
            {
                { "FullName", currentUser.Title }
            };
        }

        private User _currentUser;
        private User GetCurrentUser()
        {
            if (_currentUser != null)
                return _currentUser;

            var web = _clientContext.Web;
            _clientContext.Load(web);
            _clientContext.ExecuteQuery();

            _clientContext.Load(web.CurrentUser);
            _clientContext.ExecuteQuery();

            _currentUser = web.CurrentUser;
            return _currentUser;
        }

        public bool IsAuthenticated()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            return _list != null;
        }

        public List<TimeRegistrationEntry> GetTimeRegistrationEntries(Registrant registrant, DateTime from, DateTime to)
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to retrieving time entries");

            if (!registrant.RegistrantIdentifications.TryGetValue("FullName", out var fullName))
                throw new InvalidOperationException("The registrant must have FullName identification");

            CamlQuery query = new CamlQuery();
            query.ViewXml = $@"<View>
   <ViewFields>
      <FieldRef Name='ID' />
      <FieldRef Name='CaseTitle' />
      <FieldRef Name='Hours' />
      <FieldRef Name='DoneDate' />
      <FieldRef Name='CasePONumber' />
   </ViewFields>
   <Joins>
      <Join Type='LEFT' ListAlias='{_settings.CaseList}'>
         <Eq>
            <FieldRef Name='Case' RefType='ID' />
            <FieldRef List='{_settings.CaseList}' Name='ID' />
         </Eq>
      </Join>
   </Joins>
   <ProjectedFields>
      <Field Name='CasePONumber' Type='Lookup' List='{_settings.CaseList}' ShowField='PONumber' />
      <Field Name='CaseTitle' Type='Lookup' List='{_settings.CaseList}' ShowField='Title' />
   </ProjectedFields>
   <Query>
      <Where>
         <And>
            <And>
               <Eq>
                  <FieldRef Name='DoneBy' />
                  <Value Type='User'>{fullName}</Value>
               </Eq>
               <Geq>
                  <FieldRef Name='DoneDate' />
                  <Value Type='DateTime' IncludeTimeValue='FALSE'><Today OffsetDays='{-DateTime.Today.Subtract(from.Date).Days}' /></Value>
               </Geq>
            </And>
            <Leq>
               <FieldRef Name='DoneDate' />
               <Value Type='DateTime' IncludeTimeValue='FALSE'><Today OffsetDays='{-DateTime.Today.Subtract(to.Date).Days}' /></Value>
            </Leq>
         </And>
      </Where>
   </Query>
</View>";

            ListItemCollection items = _list.GetItems(query);
            _clientContext.Load(items);
            _clientContext.ExecuteQuery();

            return items.AsEnumerable().Select(item => new TimeRegistrationEntry
            {
                Registrant = registrant,
                TimeUsed = (double)item["Hours"],
                DateExecuted = SanitizeDateTime(((DateTime)item["DoneDate"]).ToLocalTime()),
                AccountIdentifications = GetAccountIds(item)
            }).ToList();
        }

        private DateTime SanitizeDateTime(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Local);
        }

        public bool CanCreateTimeRegistrationEntries()
        {
            return true;
        }

        public void CreateTimeRegistrationEntry(TimeRegistrationEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (entry.AccountIdentifications == null || !entry.AccountIdentifications.TryGetValue("InvoiceAccount", out var account))
                throw new InvalidOperationException("AccountIdentifications must contain an InvoiceAccount");
            if (entry.AccountIdentifications == null || !entry.AccountIdentifications.TryGetValue("InvoiceAccountText", out var accountText))
                throw new InvalidOperationException("AccountIdentifications must contain an InvoiceAccountText");
            if (entry.Registrant?.RegistrantIdentifications == null || !entry.Registrant.RegistrantIdentifications.TryGetValue("FullName", out var regName))
                throw new InvalidOperationException("Registrant must contain a FullName identification");

            var currentUser = GetCurrentUser();
            if (regName != currentUser.Title)
                throw new InvalidOperationException("Only time registrations for the current user can be created");

            // Get case based on account
            var cases = GetCases(false);
            if (!cases.Any(d => d.Value.InvoiceAccount == account))
                cases = GetCases(true);

            if (!cases.Any(d => d.Value.InvoiceAccount == account))
            {
                CreateCase(account, accountText);
                cases = GetCases(false);
            }
            var accountCase = cases.First(d => d.Value.InvoiceAccount == account);

            // Get work package for regName based on account
            var wps = GetWorkPackages(regName, false);
            if (!wps.Any(d => d.Value.InvoiceAccount == account))
                wps = GetWorkPackages(regName, true);

            if (!wps.Any(d => d.Value.InvoiceAccount == account))
            {
                CreateWorkPackage(regName, accountCase.Key, accountCase.Value.Title, account);
                wps = GetWorkPackages(regName, false);
            }
            var accountWp = wps.First(d => d.Value.InvoiceAccount == account);

            CreateTimeRegistration(entry, accountCase.Key, accountWp.Key);
        }

        public void PromptDefaultValues(bool onlyMissing)
        {
            var cachedDefaultValues = CacheUtil.Get<Dictionary<string, int>>("ToolkitDefaultsCache");
            if (cachedDefaultValues == null)
                cachedDefaultValues = new Dictionary<string, int>();

            bool addedValues = false;
            var defaultValues = _settings.CaseDefaultValues.Concat(_settings.WorkPackageDefaultValues);
            foreach (var defaultValue in defaultValues)
            {
                if (defaultValue.FieldType == "Lookup" && defaultValue.Value.StartsWith("PromptFromList:", StringComparison.CurrentCultureIgnoreCase))
                {
                    var listName = defaultValue.Value.Substring(15);
                    if (cachedDefaultValues.ContainsKey(listName) && onlyMissing)
                        continue;

                    List list = _clientContext.Web.Lists.GetByTitle(listName);
                    CamlQuery query = new CamlQuery();
                    query.ViewXml = @"<View>
   <ViewFields>
      <FieldRef Name='ID' />
      <FieldRef Name='Title' />
   </ViewFields>
</View>";

                    ListItemCollection items = list.GetItems(query);
                    _clientContext.Load(items);
                    _clientContext.ExecuteQuery();

                    List<(string Title, int Id)> choices = new List<(string Title, int Id)>();
                    foreach (var item in items)
                    {
                        choices.Add((Title: (string)item["Title"], Id: (int)item["ID"]));
                    }

                    var prompt = new SelectionPrompt<(string Title, int Id)>()
                        .Title($"Choose default {listName}:")
                        .AddChoices(choices);
                    var chosenDefault = AnsiConsole.Prompt(prompt);

                    cachedDefaultValues[listName] = chosenDefault.Id;
                    addedValues = true;
                }
            }

            if (addedValues)
                CacheUtil.Set("ToolkitDefaultsCache", cachedDefaultValues);
        }

        public bool AwaitsSetup()
        {
            return false;
        }

        private void CreateTimeRegistration(TimeRegistrationEntry entry, int caseId, int workPackageId)
        {
            List list = _clientContext.Web.Lists.GetByTitle(_settings.TimeRegistrationList);

            ListItemCreationInformation itemCreateInfo = new ListItemCreationInformation();
            ListItem listItem = list.AddItem(itemCreateInfo);

            listItem["AllowTimeregSynchronization"] = true;
            listItem["WorkPackage"] = new FieldLookupValue { LookupId = workPackageId };
            listItem["IsExtraTime"] = false;
            listItem["ManagementTime"] = 0;
            listItem["Case"] = new FieldLookupValue { LookupId = caseId };
            listItem["Hours"] = entry.TimeUsed;
            listItem["Total"] = entry.TimeUsed;
            listItem["DoneDate"] = new DateTime(entry.DateExecuted.Year, entry.DateExecuted.Month, entry.DateExecuted.Day, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
            listItem["DoneBy"] = GetCurrentUser();

            listItem.Update();

            _clientContext.ExecuteQuery();
        }

        private void CreateCase(string account, string accountText)
        {
            var caseTitle = accountText;
            if (!caseTitle.StartsWith($"{account} "))
                caseTitle = $"{account} - {accountText}";

            List list = _clientContext.Web.Lists.GetByTitle(_settings.CaseList);

            ListItemCreationInformation itemCreateInfo = new ListItemCreationInformation();
            ListItem listItem = list.AddItem(itemCreateInfo);

            SetDefaultValues(listItem, _settings.CaseDefaultValues);

            listItem["Title"] = caseTitle;
            listItem["PONumber"] = account;

            listItem.Update();

            _clientContext.ExecuteQuery();

            // Update cache
            var id = listItem.Id;
            var cachedCases = GetCases(false);
            cachedCases[id] = (Title: caseTitle, InvoiceAccount: account);
            CacheUtil.Set("ToolkitCasesCache", cachedCases);
        }

        private void CreateWorkPackage(string regName, int caseId, string caseTitle, string account)
        {
            List list = _clientContext.Web.Lists.GetByTitle(_settings.WorkPackageList);

            ListItemCreationInformation itemCreateInfo = new ListItemCreationInformation();
            ListItem listItem = list.AddItem(itemCreateInfo);

            SetDefaultValues(listItem, _settings.WorkPackageDefaultValues);

            listItem["Title"] = caseTitle;
            listItem["AssignedTo"] = FieldUserValue.FromUser(regName);
            listItem["StartDate"] = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 0, 0, 0, DateTimeKind.Utc);
            listItem["DueDate"] = new DateTime(DateTime.Today.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            listItem["RelatedCase"] = new FieldLookupValue { LookupId = caseId };

            listItem.Update();

            _clientContext.ExecuteQuery();

            // Update cache
            var id = listItem.Id;
            var cachedWorkPackages = GetWorkPackages(regName, false);
            cachedWorkPackages[id] = (Title: caseTitle, InvoiceAccount: account);
            CacheUtil.Set($"ToolkitWPCache_{regName}", cachedWorkPackages);
        }

        private void SetDefaultValues(ListItem listItem, List<ToolkitSettings.DefaultValueMapping> defaultValues)
        {
            var cachedDefaultValues = CacheUtil.Get<Dictionary<string, int>>("ToolkitDefaultsCache");

            foreach (var defaultValue in defaultValues)
            {
                object val;
                switch (defaultValue.FieldType)
                {
                    case "Choice":
                        val = defaultValue.Value;
                        break;
                    case "Lookup":
                        if (defaultValue.Value.StartsWith("PromptFromList:", StringComparison.CurrentCultureIgnoreCase))
                        {
                            var listName = defaultValue.Value.Substring(15);
                            val = new FieldLookupValue { LookupId = cachedDefaultValues[listName] };
                        }
                        else
                        {
                            val = new FieldLookupValue { LookupId = int.Parse(defaultValue.Value) };
                        }
                        break;
                    case "Number":
                        val = double.Parse(defaultValue.Value);
                        break;
                    case "Text":
                        val = defaultValue.Value;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown FieldType '{defaultValue.FieldType}'");
                }

                listItem[defaultValue.Field] = val;
            }
        }

        private Dictionary<string, string> GetAccountIds(ListItem item)
        {
            var caseTitle = (item["CaseTitle"] as FieldLookupValue)?.LookupValue;
            var account = (item["CasePONumber"] as FieldLookupValue)?.LookupValue;
            var accountIds = new Dictionary<string, string>
            {
                { "ToolkitCaseTitle", caseTitle },
                { "InvoiceAccount", account }
            };

            return accountIds;
        }

        private bool IsValidState(string state, string[] validStates)
        {
            if (state == null)
                return false;

            var stateIdMatch = Regex.Match(state, @"^[0-9]+(?=\s)");
            if (!stateIdMatch.Success)
                return false;

            var stateId = int.Parse(stateIdMatch.Value);

            foreach (var validState in validStates)
            {
                if (validState.StartsWith("<"))
                {
                    if (stateId < int.Parse(validState.Substring(1)))
                        return true;
                }
                else if (validState.StartsWith(">"))
                {
                    if (stateId > int.Parse(validState.Substring(1)))
                        return true;
                }
                else
                {
                    if (stateId == int.Parse(validState))
                        return true;
                }
            }

            return false;
        }

        private Dictionary<int, (string Title, string InvoiceAccount)> GetCases(bool forceRefresh)
        {
            var cachedCases = CacheUtil.Get<Dictionary<int, (string Title, string InvoiceAccount)>>("ToolkitCasesCache");

            if (cachedCases == null || forceRefresh)
            {
                List caseList = _clientContext.Web.Lists.GetByTitle(_settings.CaseList);
                _clientContext.Load(caseList);
                _clientContext.ExecuteQuery();

                cachedCases = new Dictionary<int, (string Title, string InvoiceAccount)>();
                if (caseList != null && caseList.ItemCount > 0)
                {
                    CamlQuery camlQuery = new CamlQuery();
                    camlQuery.ViewXml =
                        @"<View>  
             <ViewFields><FieldRef Name='ID' /><FieldRef Name='Title' /><FieldRef Name='PONumber' /><FieldRef Name='Status' /></ViewFields> 
      </View>";

                    ListItemCollection listItems = caseList.GetItems(camlQuery);
                    _clientContext.Load(listItems);
                    _clientContext.ExecuteQuery();

                    var validStates = _settings.CaseActiveStateCodes.Split(';');

                    foreach (var caseItem in listItems)
                    {
                        var state = (string)caseItem["Status"];
                        if (!IsValidState(state, validStates))
                            continue;

                        cachedCases[(int)caseItem["ID"]] = (Title: (string)caseItem["Title"], InvoiceAccount: (string)caseItem["PONumber"]);
                    }
                }

                CacheUtil.Set("ToolkitCasesCache", cachedCases);
            }

            return cachedCases;
        }

        private Dictionary<int, (string Title, string InvoiceAccount)> GetWorkPackages(string regName, bool forceRefresh)
        {
            var cachedWorkPackages = CacheUtil.Get<Dictionary<int, (string Title, string InvoiceAccount)>>($"ToolkitWPCache_{regName}");

            if (cachedWorkPackages == null || forceRefresh)
            {
                var spList = _clientContext.Web.Lists.GetByTitle(_settings.WorkPackageList);
                _clientContext.Load(spList);
                _clientContext.ExecuteQuery();

                cachedWorkPackages = new Dictionary<int, (string Title, string InvoiceAccount)>();
                if (spList != null && spList.ItemCount > 0)
                {
                    var camlQuery = new CamlQuery();
                    camlQuery.ViewXml = $@"<View>  
    <ViewFields><FieldRef Name='ID' /><FieldRef Name='CasePONumber' /><FieldRef Name='Title' /><FieldRef Name='Status' /></ViewFields> 
    <Joins>
        <Join Type='LEFT' ListAlias='{_settings.CaseList}'>
            <Eq>
                <FieldRef Name='RelatedCase' RefType='ID' />
                <FieldRef List='{_settings.CaseList}' Name='ID' />
            </Eq>
        </Join>
    </Joins>
    <ProjectedFields>
        <Field Name='CasePONumber' Type='Lookup' List='{_settings.CaseList}' ShowField='PONumber' />
    </ProjectedFields>
    <Query> 
        <Where><Eq><FieldRef Name='AssignedTo' /><Value Type='User'>{regName}</Value></Eq></Where> 
    </Query> 
</View>";

                    ListItemCollection listItems = spList.GetItems(camlQuery);
                    _clientContext.Load(listItems);
                    _clientContext.ExecuteQuery();

                    var validStates = _settings.WorkPackageActiveStateCodes.Split(';');

                    foreach (var wpItem in listItems)
                    {
                        var state = (string)wpItem["Status"];
                        if (!IsValidState(state, validStates))
                            continue;

                        cachedWorkPackages[(int)wpItem["ID"]] = (Title: (string)wpItem["Title"], InvoiceAccount: (wpItem["CasePONumber"] as FieldLookupValue)?.LookupValue);
                    }
                }

                CacheUtil.Set($"ToolkitWPCache_{regName}", cachedWorkPackages);
            }

            return cachedWorkPackages;
        }
    }
}
