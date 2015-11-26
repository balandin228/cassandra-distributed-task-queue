using System;
using System.Collections.Generic;
using System.Linq;

using GroBuf;

using NUnit.Framework;

using RemoteQueue.Cassandra.Primitives;
using RemoteQueue.Cassandra.Repositories.BlobStorages;
using RemoteQueue.Cassandra.Repositories.GlobalTicksHolder;
using RemoteQueue.Settings;

using SKBKontur.Cassandra.CassandraClient.Abstractions;
using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Cassandra.CassandraClient.Connections;
using SKBKontur.Catalogue.Objects;

namespace FunctionalTests.RepositoriesTests
{
    public class BlobStorageDecoratorTest : FunctionalTestBaseWithoutServices
    {
        public override void SetUp()
        {
            base.SetUp();
            var repositoryParameters = Container.Get<IColumnFamilyRepositoryParameters>();
            var serializer = Container.Get<ISerializer>();
            var globalTime = Container.Get<IGlobalTime>();
            var cassandraCluster = Container.Get<ICassandraCluster>();
            var settings = Container.Get<ICassandraSettings>();

            connection = cassandraCluster.RetrieveKeyspaceConnection(settings.QueueKeyspace);
            AddColumnFamily(blobStorageColumnFamilyName);
            AddColumnFamily(timeBasedBlobStorageColumnFamilyName);

            blobStorageDecorator = new BlobStorageDecorator<int?>(repositoryParameters, serializer, globalTime, blobStorageColumnFamilyName, timeBasedBlobStorageColumnFamilyName);
            blobStorage = new BlobStorage<int?>(repositoryParameters, serializer, globalTime, blobStorageColumnFamilyName);
            timeBasedBlobStorage = new TimeBasedBlobStorage<int?>(repositoryParameters, serializer, globalTime, timeBasedBlobStorageColumnFamilyName);
        }

        public override void TearDown()
        {
            connection.RemoveColumnFamily(blobStorageColumnFamilyName);
            connection.RemoveColumnFamily(timeBasedBlobStorageColumnFamilyName);
            base.TearDown();
        }

        private void AddColumnFamily(string columnFamilyName)
        {
            var keyspace = connection.DescribeKeyspace();
            if(!keyspace.ColumnFamilies.Any(x => x.Key == columnFamilyName))
            {
                connection.AddColumnFamily(new ColumnFamily
                    {
                        Name = columnFamilyName,
                        GCGraceSeconds = 10,
                        Caching = ColumnFamilyCaching.All
                    });
            }
        }

        [Test]
        public void TestRead()
        {
            blobStorage.Write(id, 1);
            Assert.That(blobStorageDecorator.Read(id), Is.EqualTo(1));

            timeBasedBlobStorage.Write(timeGuidId, 2);
            Assert.That(blobStorageDecorator.Read(id), Is.EqualTo(1));
            Assert.That(blobStorageDecorator.Read(timeGuidId.ToGuid().ToString()), Is.EqualTo(2));
        }

        [Test]
        public void TestWrite()
        {
            blobStorageDecorator.Write(id, 1);
            Assert.That(blobStorage.Read(id), Is.EqualTo(1));

            blobStorageDecorator.Write(timeGuidId.ToGuid().ToString(), 2);
            Assert.That(timeBasedBlobStorage.Read(timeGuidId), Is.EqualTo(2));
        }

        [Test]
        public void TestMultiRead()
        {
            var stringTimeGuidId = timeGuidId.ToGuid().ToString();
            Assert.That(blobStorageDecorator.Read(new string[0]).Count, Is.EqualTo(0));
            Assert.That(blobStorageDecorator.Read(new[] {id}).Count, Is.EqualTo(0));
            Assert.That(blobStorageDecorator.Read(new[] {stringTimeGuidId}).Count, Is.EqualTo(0));
            Assert.That(blobStorageDecorator.Read(new[] {id, stringTimeGuidId}).Count, Is.EqualTo(0));

            blobStorage.Write(id, 1);
            var actual = blobStorageDecorator.Read(new[] {id, stringTimeGuidId});
            Assert.That(actual.Count, Is.EqualTo(1));
            Assert.That(actual[id], Is.EqualTo(1));

            timeBasedBlobStorage.Write(timeGuidId, 2);
            actual = blobStorageDecorator.Read(new[] {id, stringTimeGuidId});
            Assert.That(actual.Count, Is.EqualTo(2));
            Assert.That(actual[id], Is.EqualTo(1));
            Assert.That(actual[stringTimeGuidId], Is.EqualTo(2));
        }

        [Test]
        public void TestReadQuiet()
        {
            blobStorage.Write(id, 1);
            Assert.That(blobStorageDecorator.ReadQuiet(new[] {id, timeGuidId.ToGuid().ToString()}), Is.EqualTo(new int?[] {1, null}));

            timeBasedBlobStorage.Write(timeGuidId, 2);
            Assert.That(blobStorageDecorator.ReadQuiet(new[] {id, timeGuidId.ToGuid().ToString()}), Is.EqualTo(new int?[] {1, 2}));
            Assert.That(blobStorageDecorator.ReadQuiet(new[] {timeGuidId.ToGuid().ToString(), id}), Is.EqualTo(new int?[] {2, 1}));
        }

        [Test]
        public void TestDelete()
        {
            blobStorage.Write(id, 1);
            blobStorageDecorator.Delete(id, DateTime.UtcNow.Ticks);
            Assert.IsNull(blobStorage.Read(id));

            timeBasedBlobStorage.Write(timeGuidId, 2);
            blobStorageDecorator.Delete(timeGuidId.ToGuid().ToString(), DateTime.UtcNow.Ticks);
            Assert.IsNull(timeBasedBlobStorage.Read(timeGuidId));
        }

        [Test]
        public void TestMultiDelete()
        {
            blobStorage.Write(id, 1);
            timeBasedBlobStorage.Write(timeGuidId, 2);
            blobStorageDecorator.Delete(new[] {id, timeGuidId.ToGuid().ToString()}, DateTime.UtcNow.Ticks);

            Assert.IsNull(blobStorage.Read(id));
            Assert.IsNull(timeBasedBlobStorage.Read(timeGuidId));
        }

        [Test]
        public void TestReadAll()
        {
            blobStorage.Write(id, 11);
            timeBasedBlobStorage.Write(timeGuidId, 12);

            var anotherTimeGuidId = TimeGuid.NewGuid(new Timestamp(DateTime.UtcNow.AddDays(1)));

            blobStorage.Write(anotherId, 21);
            timeBasedBlobStorage.Write(anotherTimeGuidId, 22);

            Assert.That(blobStorageDecorator.ReadAll(1).ToArray(), Is.EquivalentTo(new[] {11, 12, 21, 22}));
            Assert.That(blobStorageDecorator.ReadAllWithIds(1).ToArray(), Is.EquivalentTo(new[]
                {
                    new KeyValuePair<string, int?>(id, 11),
                    new KeyValuePair<string, int?>(timeGuidId.ToGuid().ToString(), 12),
                    new KeyValuePair<string, int?>(anotherId, 21),
                    new KeyValuePair<string, int?>(anotherTimeGuidId.ToGuid().ToString(), 22),
                }));
        }

        private const string blobStorageColumnFamilyName = "blobStorageTest";
        private const string timeBasedBlobStorageColumnFamilyName = "orderedBlobStorageTest";
        private const string id = "21E011E8-F715-4EBF-944A-012033C0CE7C";
        private const string anotherId = "8B728A4E-68D6-4BAD-90FA-DFCFFF27F77B";
        private BlobStorageDecorator<int?> blobStorageDecorator;
        private BlobStorage<int?> blobStorage;
        private TimeBasedBlobStorage<int?> timeBasedBlobStorage;
        private readonly TimeGuid timeGuidId = TimeGuid.NowGuid();
        private IKeyspaceConnection connection;
    }
}