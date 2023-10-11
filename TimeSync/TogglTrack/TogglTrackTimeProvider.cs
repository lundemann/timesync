using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators;
using TimeSync.Domain;
using TimeSync.Interfaces;
using TimeSync.Utils;

namespace TimeSync.TogglTrack
{
    public class TogglTrackTimeProvider : ITimeProvider
    {
        private bool _initialized = false;
        private string _email;
        private string _togglAccessToken;
        private string _togglApiUrl;

        public void Initialize(Credentials credentials, Dictionary<string, string> config)
        {
            if (!config.TryGetValue("TogglApiUrl", out _togglApiUrl))
                throw new InvalidOperationException("config must contain TogglApiUrl");

            if (credentials != null)
            {
                _email = credentials.Username;

                if (credentials.Secret == null || !Regex.IsMatch(credentials.Secret, @"^[a-zA-Z0-9]+$"))
                    throw new InvalidOperationException("Invalid credentials.Secret");

                _togglAccessToken = credentials.Secret;
            }

            _initialized = true;
        }

        public Credentials AskUserForCredentials()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            Console.Write("Username (email): ");
            _email = Console.ReadLine();

            Console.Write("Toggl access token: ");
            var togglAccessToken = InputUtil.ReadMaskedInput();
            Console.WriteLine();
            if (!Regex.IsMatch(togglAccessToken, @"^[a-zA-Z0-9]+$"))
                throw new InvalidOperationException("Invalid access token");
            _togglAccessToken = togglAccessToken;

            if (!IsAuthenticated())
                return null;

            CacheUtil.Set("TogglProviderIsSetup", (object)true);

            return new Credentials
            {
                Username = _email,
                Secret = _togglAccessToken
            };
        }

        public Dictionary<string, string> GetLoggedInIdentity()
        {
            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (!IsAuthenticated())
                throw new InvalidOperationException("Authentication failed. Authentication must be established prior to calling this method");

            var request = new RestRequest("/api/v8/workspaces");
            request.AddQueryParameter("user_agent", _email);
            var response = TogglRestClient.Get(request);
            ValidateResponse(response);
            dynamic data = JToken.Parse(response.Content);

            return new Dictionary<string, string>
            {
                { "TogglWorkspace", (string)data[0].id }
            };
        }

        private bool? _authenticated;
        public bool IsAuthenticated()
        {
            if (_authenticated != null)
                return _authenticated.Value;

            if (!_initialized)
                throw new InvalidOperationException("Initialize the provider before using it");

            if (_email == null || _togglAccessToken == null)
                return false;

            // Validate Toggl access token
            var togglRequest = new RestRequest("/api/v8/workspaces");
            togglRequest.AddQueryParameter("user_agent", _email);
            
            try
            {
                var togglResp = TogglRestClient.Get(togglRequest);
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

            if (!registrant.RegistrantIdentifications.TryGetValue("TogglWorkspace", out var fullName))
                throw new InvalidOperationException("The registrant must have TogglWorkspace identification");

            List<TimeRegistrationEntry> entries = new List<TimeRegistrationEntry>();

            bool morePages = true;
            int page = 1;
            while (morePages)
            {
                var togglRequest = new RestRequest("/reports/api/v2/details");
                togglRequest.AddQueryParameter("user_agent", _email);
                togglRequest.AddQueryParameter("workspace_id", registrant.RegistrantIdentifications["TogglWorkspace"]);
                togglRequest.AddQueryParameter("page", page.ToString());
                togglRequest.AddQueryParameter("since", from.ToString("yyyy-MM-dd"));
                togglRequest.AddQueryParameter("until", to.ToString("yyyy-MM-dd"));

                var response = TogglRestClient.Get(togglRequest);
                ValidateResponse(response);
                dynamic data = JToken.Parse(response.Content);

                foreach (var entry in data.data)
                {
                    var desc = (string)entry.description;
                    var jiraMatch = Regex.Match(desc, @"^[A-ZÆØÅ]+-[0-9]+ ");
                    var jiraKey = jiraMatch.Success ? jiraMatch.Value.Trim() : null;

                    var timeEntry = new TimeRegistrationEntry
                    {
                        DateExecuted = SanitizeDateTime((DateTime)entry.start),
                        TimeUsed = ((double)(long)entry.dur) / 60000 / 60,
                        Registrant = registrant,
                        AccountIdentifications = new Dictionary<string, string>
                        {
                            { "TogglDescription", desc }
                        }
                    };

                    if (jiraKey != null)
                        timeEntry.AccountIdentifications["JiraIssueKey"] = jiraKey;

                    entries.Add(timeEntry);
                }

                ++page;
                morePages = (int)data.total_count > entries.Count && data.data.Count > 0;
            }

            return entries;
        }

        public bool CanCreateTimeRegistrationEntries()
        {
            return false;
        }

        public void CreateTimeRegistrationEntry(TimeRegistrationEntry entry)
        {
            throw new NotImplementedException();
        }

        public void PromptDefaultValues(bool onlyMissing)
        {
        }

        public bool AwaitsSetup()
        {
            var isSetup = (bool?)CacheUtil.Get<object>("TogglProviderIsSetup");
            return isSetup != true;
        }

        private RestClient TogglRestClient
        {
            get
            {
                var opts = new RestClientOptions(_togglApiUrl)
                {
                    ThrowOnAnyError = true,
                    Authenticator = new HttpBasicAuthenticator(_togglAccessToken, "api_token")
                };

                var togglClient = new RestClient(opts);
                return togglClient;
            }
        }

        private void ValidateResponse(RestResponse response)
        {
            if (!response.IsSuccessful)
                throw new InvalidOperationException($"Request failed with status {response.StatusCode}");
        }

        private DateTime SanitizeDateTime(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Local);
        }
    }
}
