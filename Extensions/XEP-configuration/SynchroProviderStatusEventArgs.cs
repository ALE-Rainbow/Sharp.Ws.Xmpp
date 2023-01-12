using System;
using System.Collections.Generic;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    public class SynchroProviderStatusEventArgs: EventArgs
    {
        /// <summary>
        /// <see cref="String"/> - The type of synchro: "calendar" or "presence"
        /// </summary>
        public String Type { get; internal set; }

        /// <summary>
        /// <see cref="String"/> - The provider of the syncrho: "office365", "google" (for calendar) or "teams" (for presence)
        /// </summary>
        public String Provider { get; internal set; }

        /// <summary>
        /// <see cref="Boolean"/> - To know if the synchro is enabled or not
        /// </summary>
        public Boolean Enabled { get; internal set; }

        /// <summary>
        /// Initializes a new instance of the SynchroProviderStatus class.
        /// </summary>
        /// <param name="type"><see cref="String"/>The type of synchro: "calendar" or "presence"</param>
        /// <param name="provider"><see cref="String"/>The provider of the synchro: "office365" or "google"</param>
        /// <param name="enabled"><see cref="Boolean"/>To know if the synchro is enabled or not</param>
        public SynchroProviderStatusEventArgs(String type, String provider, Boolean enabled)
        {
            Type = type;
            Provider = provider;
            Enabled = enabled;
        }
    }
}
