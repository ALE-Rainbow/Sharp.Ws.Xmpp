using System;
using System.Collections.Generic;
using System.Linq;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Persistence for OMEMO: cached device lists plus opaque state blobs (identity, sessions,
    /// trust decisions).
    /// </summary>
    internal interface IOmemoStore
    {
        IEnumerable<OmemoDevice> GetDevices(string bareJid);
        void SetDevices(string bareJid, IEnumerable<OmemoDevice> devices);

        /// <summary>Returns the value stored under the key, or null.</summary>
        byte[] GetValue(string key);

        /// <summary>Stores a value; null deletes the key.</summary>
        void SetValue(string key, byte[] value);

        /// <summary>Returns all keys starting with the prefix.</summary>
        IEnumerable<string> GetKeys(string prefix);
    }

    internal sealed class InMemoryOmemoStore : IOmemoStore
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, List<OmemoDevice>> devices = new Dictionary<string, List<OmemoDevice>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> values = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public IEnumerable<OmemoDevice> GetDevices(string bareJid)
        {
            lock (syncRoot)
            {
                if (bareJid == null || !devices.ContainsKey(bareJid))
                    return Enumerable.Empty<OmemoDevice>();
                return devices[bareJid].ToArray();
            }
        }

        public void SetDevices(string bareJid, IEnumerable<OmemoDevice> value)
        {
            if (bareJid == null)
                return;
            lock (syncRoot)
                devices[bareJid] = value == null ? new List<OmemoDevice>() : value.ToList();
        }

        public byte[] GetValue(string key)
        {
            lock (syncRoot)
                return key != null && values.TryGetValue(key, out byte[] v) ? v : null;
        }

        public void SetValue(string key, byte[] value)
        {
            if (key == null)
                return;
            lock (syncRoot)
            {
                if (value == null)
                    values.Remove(key);
                else
                    values[key] = value;
            }
        }

        public IEnumerable<string> GetKeys(string prefix)
        {
            lock (syncRoot)
                return values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }
    }
}
