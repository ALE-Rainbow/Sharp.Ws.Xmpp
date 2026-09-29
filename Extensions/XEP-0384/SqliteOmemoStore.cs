using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Sharp.Xmpp.Extensions
{
    internal sealed class SqliteOmemoStore : IOmemoStore
    {
        private readonly DbContextOptions<OmemoStoreDbContext> options;
        private readonly object syncRoot = new object();

        public SqliteOmemoStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            var builder = new DbContextOptionsBuilder<OmemoStoreDbContext>();
            builder.UseSqlite($"Data Source={databasePath}");
            options = builder.Options;

            using (var db = new OmemoStoreDbContext(options))
            {
                db.Database.EnsureCreated();
                // Databases created by earlier versions lack the state table.
                db.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS \"OmemoState\" (\"Key\" TEXT NOT NULL CONSTRAINT \"PK_OmemoState\" PRIMARY KEY, \"Value\" BLOB NULL)");
            }
        }

        public IEnumerable<OmemoDevice> GetDevices(string bareJid)
        {
            if (string.IsNullOrWhiteSpace(bareJid))
                return Enumerable.Empty<OmemoDevice>();

            lock (syncRoot)
            {
                using (var db = new OmemoStoreDbContext(options))
                {
                    return db.Devices
                        .AsNoTracking()
                        .Where(x => x.BareJid == bareJid)
                        .OrderBy(x => x.DeviceId)
                        .Select(x => new OmemoDevice
                        {
                            Id = x.DeviceId,
                            Label = x.Label,
                            LabelSignature = x.LabelSignature
                        })
                        .ToArray();
                }
            }
        }

        public void SetDevices(string bareJid, IEnumerable<OmemoDevice> devices)
        {
            if (string.IsNullOrWhiteSpace(bareJid))
                return;

            lock (syncRoot)
            {
                using (var db = new OmemoStoreDbContext(options))
                {
                    var existing = db.Devices.Where(x => x.BareJid == bareJid).ToList();
                    if (existing.Count > 0)
                        db.Devices.RemoveRange(existing);

                    if (devices != null)
                    {
                        var entities = devices.Select(d => new OmemoDeviceEntity
                        {
                            BareJid = bareJid,
                            DeviceId = d.Id,
                            Label = d.Label,
                            LabelSignature = d.LabelSignature
                        }).ToList();

                        if (entities.Count > 0)
                            db.Devices.AddRange(entities);
                    }

                    db.SaveChanges();
                }
            }
        }

        public byte[] GetValue(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            lock (syncRoot)
            {
                using (var db = new OmemoStoreDbContext(options))
                {
                    return db.State.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefault();
                }
            }
        }

        public void SetValue(string key, byte[] value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (syncRoot)
            {
                using (var db = new OmemoStoreDbContext(options))
                {
                    var existing = db.State.SingleOrDefault(x => x.Key == key);
                    if (value == null)
                    {
                        if (existing != null)
                            db.State.Remove(existing);
                    }
                    else if (existing == null)
                    {
                        db.State.Add(new OmemoStateEntity { Key = key, Value = value });
                    }
                    else
                    {
                        existing.Value = value;
                    }
                    db.SaveChanges();
                }
            }
        }

        public IEnumerable<string> GetKeys(string prefix)
        {
            lock (syncRoot)
            {
                using (var db = new OmemoStoreDbContext(options))
                {
                    return db.State.AsNoTracking().Select(x => x.Key).ToArray()
                        .Where(k => k.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal))
                        .ToArray();
                }
            }
        }
    }

    internal sealed class OmemoStoreDbContext : DbContext
    {
        public OmemoStoreDbContext(DbContextOptions<OmemoStoreDbContext> options)
            : base(options)
        {
        }

        public DbSet<OmemoDeviceEntity> Devices { get; set; }
        public DbSet<OmemoStateEntity> State { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OmemoDeviceEntity>()
                .HasKey(x => new { x.BareJid, x.DeviceId });

            modelBuilder.Entity<OmemoStateEntity>()
                .HasKey(x => x.Key);

            modelBuilder.Entity<OmemoDeviceEntity>().ToTable("OmemoDevices");
            modelBuilder.Entity<OmemoStateEntity>().ToTable("OmemoState");
        }
    }

    internal sealed class OmemoDeviceEntity
    {
        public string BareJid { get; set; }
        public int DeviceId { get; set; }
        public string Label { get; set; }
        public string LabelSignature { get; set; }
    }

    internal sealed class OmemoStateEntity
    {
        public string Key { get; set; }
        public byte[] Value { get; set; }
    }
}
