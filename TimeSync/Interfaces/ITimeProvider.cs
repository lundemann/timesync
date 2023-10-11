using System;
using System.Collections.Generic;
using TimeSync.Domain;

namespace TimeSync.Interfaces
{
    public interface ITimeProvider
    {
        /// <summary>
        /// Initializes the time provider with credentials.
        /// </summary>
        /// <param name="credentials">Null if no stored credentials exists. Otherwise the stored credentials.</param>
        /// <param name="config">Configuration for the provider</param>
        void Initialize(Credentials credentials, Dictionary<string, string> config);

        /// <summary>
        /// Ask the user for credentials for the specific time provider.
        /// </summary>
        /// <returns>Input credentials to be cached.</returns>
        Credentials AskUserForCredentials();

        /// <summary>
        /// Gets the currently signed in identity
        /// </summary>
        /// <returns>Key/value pair(s) of the signed in identity in the provider</returns>
        Dictionary<string, string> GetLoggedInIdentity();

        /// <summary>
        /// Determines if the user is authenticated against the time provider.
        /// </summary>
        /// <returns>True if the user is authenticated. False if no credentials exists or the credentials are no longer valid.</returns>
        bool IsAuthenticated();

        /// <summary>
        /// Retrieves a list of time registration entries for the registrant
        /// </summary>
        /// <param name="registrant">The registrant</param>
        /// <param name="from">Start date of the period to get entries for</param>
        /// <param name="to">End date of the period to get entries for</param>
        /// <returns>The list of time registration entries</returns>
        List<TimeRegistrationEntry> GetTimeRegistrationEntries(Registrant registrant, DateTime from, DateTime to);

        /// <summary>
        /// Determines if this provider can create time registration entries
        /// </summary>
        /// <returns>true if the provider can create time registration entries, false otherwise</returns>
        bool CanCreateTimeRegistrationEntries();

        /// <summary>
        /// Creates the time registration entry in the system
        /// </summary>
        /// <param name="entry">The time registration entry to create</param>
        void CreateTimeRegistrationEntry(TimeRegistrationEntry entry);

        /// <summary>
        /// Trigger input of default values
        /// </summary>
        /// <param name="onlyMissing">Determines if only missing default values should be set (true) or all (false)</param>
        void PromptDefaultValues(bool onlyMissing);

        /// <summary>
        /// Determines if the provider awaits setup
        /// </summary>
        /// <returns>true if the provider awaits setup, false otherwise</returns>
        bool AwaitsSetup();
    }
}
