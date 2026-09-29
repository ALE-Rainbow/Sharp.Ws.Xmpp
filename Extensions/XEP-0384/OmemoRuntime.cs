using System;

namespace Sharp.Xmpp.Extensions
{
    internal static class OmemoRuntime
    {
        /// <summary>Overrides the store, e.g. for tests.</summary>
        public static Func<IOmemoStore> StoreFactory { get; set; }

        public static IOmemoStore CreateStore()
        {
            if (StoreFactory != null)
                return StoreFactory();

            string path = Environment.GetEnvironmentVariable(OmemoConstants.DbPathEnvVar);
            if (String.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("OMEMO needs persistent storage: set the " +
                    OmemoConstants.DbPathEnvVar + " environment variable to a database file path.");
            }
            return new SqliteOmemoStore(path);
        }
    }
}
