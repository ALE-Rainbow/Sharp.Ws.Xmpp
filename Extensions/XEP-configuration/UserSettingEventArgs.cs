using System;
using System.Collections.Generic;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    public class UserSettingEventArgs: EventArgs
    {
        /// <summary>
        /// Name of the user setting
        /// </summary>
        public String Name
        {
            get;
            private set;
        }

        /// <summary>
        /// Value of the user setting
        /// </summary>
        public String Value
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the UserSettingEventArgs class.
        /// </summary>
        /// <param name="name"><see cref="String"/>Name of the user setting</param>
        /// <param name="value"><see cref="String"/>Value of the user setting</param>
        public UserSettingEventArgs(String name, String value)
        {
            Name = name;
            Value = value;
        }

    }
}
