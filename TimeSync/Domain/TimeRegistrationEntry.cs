using System;
using System.Collections.Generic;

namespace TimeSync.Domain
{
    /// <summary>
    /// A single time registration entry
    /// </summary>
    public class TimeRegistrationEntry
    {
        /// <summary>
        /// The registrant of the time registration
        /// </summary>
        public Registrant Registrant { get; set; }

        /// <summary>
        /// The registered time
        /// </summary>
        public double TimeUsed { get; set; }

        /// <summary>
        /// The date the work was performed
        /// </summary>
        public DateTime DateExecuted { get; set; }

        /// <summary>
        /// The time registration account identifications (key = type, value = id)
        /// </summary>
        public Dictionary<string, string> AccountIdentifications { get; set; }
    }
}
