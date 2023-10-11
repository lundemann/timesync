using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators;
using RestSharp.Authenticators.OAuth2;
using TimeSync.Domain;
using TimeSync.Interfaces;
using TimeSync.Utils;

namespace TimeSync.Tempo
{
    public class TempoTimeProvider : ITimeProvider
    {
        private bool _initialized = false;
        private string _email;
        private string _jiraAccessToken;
        private string _tempoAccessToken;
        private string _jiraUrl;

        private const string _jiraTokenRegex = @"[a-zA-Z0-9_\-=]+";
        private const string _tempoTokenRegex = @"[a-zA-Z0-9\-]+";

        public void Initialize(Credentials credentials, Dictionary<string, string> config)
        {
            if (!config.TryGetValue("JiraUrl", out _jiraUrl))
                throw new InvalidOperationException("config must contain JiraUrl");

            if (credentials != null)
            {
                _email = credentials.Username;

                if (credentials.Secret == null || !Regex.IsMatch(credentials.Secret, $@"^{_jiraTokenRegex} {_tempoTokenRegex}$"))
                    throw new InvalidOperationException("Invalid credentials.Secret");

                var secrets = credentials.Secret.Split(' ');
                _jiraAccessToken = secrets[0];
                _tempoAccessToken = secrets[1];
            }

            _initialized = true;
        }

        public Credentials AskUserForCredentials()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            Console.Write("Username (email): ");
            _email = Console.ReadLine();

            Console.Write("Jira access token: ");
            var jiraAccessToken = InputUtil.ReadMaskedInput();
            Console.WriteLine();
            if (!Regex.IsMatch(jiraAccessToken, $@"^{_jiraTokenRegex}$"))
                throw new InvalidOperationException("Invalid access token");
            _jiraAccessToken = jiraAccessToken;

            Console.Write("Tempo access token: ");
            var tempoAccessToken = InputUtil.ReadMaskedInput();
            Console.WriteLine();
            if (!Regex.IsMatch(tempoAccessToken, $@"^{_tempoTokenRegex}$"))
                throw new InvalidOperationException("Invalid access token");
            _tempoAccessToken = tempoAccessToken;

            if (!IsAuthenticated())
                return null;

            return new Credentials
            {
                Username = _email,
                Secret = $"{_jiraAccessToken} {_tempoAccessToken}"
            };
        }

        public Dictionary<string, string> GetLoggedInIdentity()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to calling this method");

            var request = new RestRequest("/rest/api/2/myself");
            var response = JiraRestClient.Get(request);
            ValidateResponse(response);
            dynamic data = JToken.Parse(response.Content);

            return new Dictionary<string, string>
            {
                { "AtlassianID", (string)data.accountId }
            };
        }

        private bool? _authenticated;
        public bool IsAuthenticated()
        {
            if (_authenticated != null)
                return _authenticated.Value;

            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (_email == null || _jiraAccessToken == null || _tempoAccessToken == null)
                return false;

            // Validate Tempo access token
            var tempoRequest = new RestRequest("/core/3/periods");
            tempoRequest.AddQueryParameter("from", DateTime.Now.ToString("yyyy-MM-dd"));
            tempoRequest.AddQueryParameter("to", DateTime.Now.ToString("yyyy-MM-dd"));
            
            try 
            { 
                var tempoResp = TempoRestClient.Get(tempoRequest);
            }
            catch (HttpRequestException)
            {
                return false;
            }

            // Validate Jira access token
            var jiraRequest = new RestRequest(Path.Combine(_jiraUrl, "rest/api/2/search?maxResults=1"));

            try
            {
                var jiraResp = JiraRestClient.Get(jiraRequest);
            }
            catch (HttpRequestException)
            {
                return false;
            }

            _authenticated = true;
            return true;
        }

        public List<TimeRegistrationEntry> GetTimeRegistrationEntries(Registrant registrant, DateTime from, DateTime to)
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to retrieving time entries");

            if (!registrant.RegistrantIdentifications.TryGetValue("AtlassianID", out var atlassianId))
                throw new InvalidOperationException("The registrant must have AtlassianID identification");

            var tempoClient = TempoRestClient;
            var jiraClient = JiraRestClient;

            var request = new RestRequest($"/core/3/worklogs/user/{atlassianId}");
            request.AddQueryParameter("from", from.ToString("yyyy-MM-dd"));
            request.AddQueryParameter("to", to.ToString("yyyy-MM-dd"));
            request.AddQueryParameter("limit", "1000");

            var response = tempoClient.Get(request);
            ValidateResponse(response);
            dynamic data = JToken.Parse(response.Content);

            List<TimeRegistrationEntry> entries = new List<TimeRegistrationEntry>();
            foreach (var worklog in data.results)
            {
                entries.Add(new TimeRegistrationEntry
                {
                    Registrant = registrant,
                    AccountIdentifications = GetAccountIdentifications(jiraClient, tempoClient, worklog),
                    DateExecuted = SanitizeDateTime((DateTime)worklog.startDate),
                    TimeUsed = ((double)worklog.timeSpentSeconds) / 60 / 60
                });
            }

            FlushAccountIdentificationsCache();

            return entries;
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
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to retrieving time entries");

            if (!entry.Registrant.RegistrantIdentifications.TryGetValue("AtlassianID", out var atlassianId))
                throw new InvalidOperationException("The registrant must have AtlassianID identification");

            if (!entry.AccountIdentifications.TryGetValue("JiraIssueKey", out var issueKey))
                throw new InvalidOperationException("The entry must have a JiraIssueKey");

            var tempoClient = TempoRestClient;

            var json = JToken.FromObject(new
            {
                issueKey = issueKey,
                timeSpentSeconds = entry.TimeUsed * 60 * 60,
                startDate = entry.DateExecuted.ToString("yyyy-MM-dd"),
                startTime = "00:00:00",
                authorAccountId = atlassianId
            }).ToString();

            var request = new RestRequest("/core/3/worklogs");
            request.Method = Method.Post;
            request.AddHeader("Accept", "application/json");
            //request.Parameters.Clear();
            request.AddParameter("application/json", json, ParameterType.RequestBody);

            var response = tempoClient.Post(request);
            ValidateResponse(response);
        }

        public void PromptDefaultValues(bool onlyMissing)
        {
            // No default values to set
        }

        public bool AwaitsSetup()
        {
            return false;
        }

        private RestClient JiraRestClient
        {
            get
            {
                var opts = new RestClientOptions(_jiraUrl)
                {
                    ThrowOnAnyError = true,
                    Authenticator = new HttpBasicAuthenticator(_email, _jiraAccessToken)
                };

                var jiraClient = new RestClient(opts);
                return jiraClient;
            }
        }

        private RestClient TempoRestClient
        {
            get
            {
                var opts = new RestClientOptions("https://api.tempo.io/")
                {
                    ThrowOnAnyError = true,
                    Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(_tempoAccessToken, "Bearer")
                };

                var tempoClient = new RestClient(opts);
                return tempoClient;
            }
        }

        private void ValidateResponse(RestResponse response)
        {
            if (!response.IsSuccessful)
                throw new InvalidOperationException($"Request failed with status {response.StatusCode}");
        }

        private Dictionary<string, Dictionary<string, string>> _accountIdentifications;
        private Dictionary<int, string> _accounts;
        private Dictionary<string, string> GetAccountIdentifications(RestClient jiraClient, RestClient tempoClient, dynamic worklog)
        {
            if (_accountIdentifications == null)
                _accountIdentifications = CacheUtil.Get<Dictionary<string, Dictionary<string, string>>>("JiraAccountIdCache");
            if (_accountIdentifications == null)
                _accountIdentifications = new Dictionary<string, Dictionary<string, string>>();
            
            if (_accounts == null)
                _accounts = CacheUtil.Get<Dictionary<int, string>>("TempoAccountIdCache");
            if (_accounts == null)
            {
                _accounts = new Dictionary<int, string>();
                var tempoAccountRequest = new RestRequest($"/core/3/accounts");
                var tempoAccountResponse = tempoClient.Get(tempoAccountRequest);
                ValidateResponse(tempoAccountResponse);

                dynamic tempoAccountData = JToken.Parse(tempoAccountResponse.Content);
                foreach (var acc in tempoAccountData.results)
                {
                    if (acc.status?.ToString() != "CLOSED")
                        _accounts[(int)acc.id] = (string)acc.key;
                }
            }

            string issueUrl = (string)worklog.issue.self;
            if (!_accountIdentifications.TryGetValue(issueUrl, out var ids))
            {
                ids = new Dictionary<string, string>();

                var request = new RestRequest(issueUrl);
                var response = jiraClient.Get(request);

                dynamic data = JToken.Parse(response.Content);
                var accountId = data.fields.customfield_12320 != null ? (int?)data.fields.customfield_12320.id : null;

                string invoiceAccount = null;
                if (accountId != null)
                {
                    _accounts.TryGetValue(accountId.Value, out invoiceAccount);
                }

                var accountString = data.fields.customfield_12320 != null ? (string)data.fields.customfield_12320.value : null;
                invoiceAccount = invoiceAccount ?? accountString?.Split(' ')[0];
                ids["JiraIssueUrl"] = issueUrl;
                ids["JiraIssueKey"] = (string)data.key;
                ids["InvoiceAccount"] = invoiceAccount;
                ids["InvoiceAccountText"] = accountString;

                _accountIdentifications[issueUrl] = ids;
            }

            return ids;
        }

        public void ClearAccountIdentificationsCache()
        {
            _accountIdentifications = null;
            _accounts = null;
            CacheUtil.Set("TempoAccountIdCache", (Dictionary<int, string>)null);
            CacheUtil.Set("JiraAccountIdCache", (Dictionary<string, Dictionary<string, string>>)null);
        }

        private void FlushAccountIdentificationsCache()
        {
            if (_accountIdentifications != null)
                CacheUtil.Set("JiraAccountIdCache", _accountIdentifications);
            if (_accounts != null)
                CacheUtil.Set("TempoAccountIdCache", _accounts);
        }
    }
}
