using System;

namespace Sharp.Xmpp.Core.Sasl
{
    /// <summary>
    /// A factory class for producing instances of Sasl mechanisms.
    /// </summary>
    internal static class SaslFactory
    {
        /// <summary>
        /// Creates an instance of the Sasl mechanism with the specified
        /// name.
        /// </summary>
        /// <param name="name">The name of the Sasl mechanism of which an
        /// instance will be created.</param>
        /// <returns>An instance of the Sasl mechanism with the specified name.</returns>
        /// <exception cref="ArgumentNullException">The name parameter is null.</exception>
        /// <exception cref="SaslException">A Sasl mechanism with the
        /// specified name is not registered with Sasl.SaslFactory.</exception>
        public static SaslMechanism Create(string name, String userName, String password)
        {
            name.ThrowIfNull("name");
            if (!SaslMechanism.Mechanisms.Contains(name))
            {
                throw new SaslException("A Sasl mechanism with the specified name " +
                    "is not registered with Sasl.SaslFactory.");
            }
            return new SaslMechanism(name, userName, password);
        }

        /// <summary>
        /// Static class constructor. Initializes static properties.
        /// </summary>
        static SaslFactory()
        {
        }
    }
}