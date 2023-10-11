using System.Collections.Generic;

namespace TimeSync.Domain
{
    /// <summary>
    /// User creating time registration entries
    /// </summary>
    public class Registrant
    {
        /// <summary>
        /// The name of the registrant
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Dictionary of identifications of the registrant of different types (key = type, value = id)
        /// </summary>
        public Dictionary<string, string> RegistrantIdentifications { get; set; }
    }
}
