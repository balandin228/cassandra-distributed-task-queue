using System;
using System.Collections.Generic;
using System.Linq;

using MoreLinq;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.GlobalTicksHolder;

using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Catalogue.Core.SynchronizationStorage.EventDevourers;
using SKBKontur.Catalogue.Core.SynchronizationStorage.LocalStorage;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringServiceCore.Sheduler;

using log4net;

using MTaskState = SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities.Primitives.TaskState;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringServiceCore.Implementation
{
    public class LocalStorageUpdater : ILocalStorageUpdater
    {
        public LocalStorageUpdater
            (IHandleTasksMetaStorage handleTasksMetaStorage,
             IEventLogRepository eventLogRepository,
             ILocalStorage localStorage,
             IGlobalTime globalTime,
             IThreadUpdateStartingTicksCache startingTicksCache,
             IEventCache eventCache,
             ICassandraClusterSettings cassandraClusterSettings)
        {
            this.handleTasksMetaStorage = handleTasksMetaStorage;
            this.eventLogRepository = eventLogRepository;
            this.localStorage = localStorage;
            this.globalTime = globalTime;
            this.startingTicksCache = startingTicksCache;
            this.eventCache = eventCache;
            this.cassandraClusterSettings = cassandraClusterSettings;
        }

        public void Update()
        {
            var guid = Guid.NewGuid().ToString();
            try
            {
                startingTicksCache.Add(guid, GetStartTime());
                eventCache.RemoveEvents(startingTicksCache.GetMinimum());
                var lastTicks = globalTime.GetNowTicks();
                UpdateLocalStorage(eventLogRepository.GetEvents(localStorage.GetLastUpdateTime<MonitoringTaskMetadata>()));
                lock(lockObject)
                {
                    if(localStorage.GetLastUpdateTime<MonitoringTaskMetadata>() < lastTicks)
                        localStorage.SetLastUpdateTime<MonitoringTaskMetadata>(lastTicks);
                }
            }
            finally
            {
                startingTicksCache.Remove(guid);
            }
        }

        public void RecalculateInProcess()
        {
            var metadatas = localStorage.Search<MonitoringTaskMetadata>(
                x => x.State == MTaskState.New ||
                     x.State == MTaskState.InProcess ||
                     x.State == MTaskState.WaitingForRerun ||
                     x.State == MTaskState.WaitingForRerunAfterError).ToArray();
            var list = new List<MonitoringTaskMetadata>();
            foreach(var metadata in metadatas)
            {
                var meta = handleTasksMetaStorage.GetMeta(metadata.TaskId);
                MonitoringTaskMetadata newMetadata;
                if(TryConvertTaskMetaInformationToMonitoringTaskMetadata(meta, out newMetadata))
                    list.Add(newMetadata);
            }
            foreach(var batch in new SeparateOnBatchesEnumerable<MonitoringTaskMetadata>(list, 100))
                localStorage.Write(batch);
        }

        private void UpdateLocalStorage(IEnumerable<TaskMetaUpdatedEvent> events)
        {
            var updateTime = globalTime.GetNowTicks();
            var list = new List<TaskMetaUpdatedEvent>();
            foreach(var eventBatch in events.Batch(300, Enumerable.ToArray))
            {
                var dict = new Dictionary<string, MonitoringTaskMetadata>();
                foreach(var taskEvent in eventBatch)
                {
                    if(eventCache.Contains(taskEvent)) continue;

                    var taskMeta = handleTasksMetaStorage.GetMeta(taskEvent.TaskId);
                    if(taskEvent.Ticks <= taskMeta.LastModificationTicks)
                    {
                        MonitoringTaskMetadata metadata;
                        if(TryConvertTaskMetaInformationToMonitoringTaskMetadata(taskMeta, out metadata))
                        {
                            if(dict.ContainsKey(metadata.Id))
                            {
                                if (GetTicks(metadata.LastModificationDateTime) >= GetTicks(dict[metadata.Id].LastModificationDateTime))
                                    dict[metadata.Id] = metadata;
                            }
                            else
                                dict.Add(metadata.Id, metadata);
                        }
                        else logger.ErrorFormat("Error while index metadata for task '{0}'", taskMeta.Id);
                    }
                }
                list.AddRange(eventBatch);

                var forUpdateWithoutR = dict.Select(x => x.Value).ToArray();
                if(dict.Count != 0)
                {
                    foreach(var batch in forUpdateWithoutR.Batch(100, Enumerable.ToArray))
                        localStorage.Write(batch);
                }
                dict.Clear();
            }
            eventCache.AddEvents(list);
            localStorage.SetLastUpdateTime<MonitoringTaskMetadata>(updateTime);
        }

        private long GetTicks(DateTime? dateTime)
        {
            if(dateTime == null) return 0;
            return dateTime.Value.Ticks;
        }

        private long GetStartTime()
        {
            var maxCassandraWriteTimeout = cassandraClusterSettings.Timeout * 10000 * cassandraClusterSettings.Attempts;
            return localStorage.GetLastUpdateTime<MonitoringTaskMetadata>() - maxCassandraWriteTimeout;
        }

        private bool TryConvertTaskMetaInformationToMonitoringTaskMetadata(TaskMetaInformation info, out MonitoringTaskMetadata taskMetadata)
        {
            if(info == null)
            {
                taskMetadata = new MonitoringTaskMetadata();
                logger.Error("MetaInformation null");
                return false;
            }
            taskMetadata = new MonitoringTaskMetadata
                {
                    Name = info.Name,
                    TaskId = info.Id,
                    Ticks = new DateTime(info.Ticks),
                    MinimalStartTicks = new DateTime(info.MinimalStartTicks),
                    StartExecutingTicks = info.StartExecutingTicks.HasValue ? (DateTime?)new DateTime(info.StartExecutingTicks.Value) : null,
                    FinishExecutingTicks = info.FinishExecutingTicks.HasValue ? (DateTime?)new DateTime(info.FinishExecutingTicks.Value) : null,
                    State = default(MTaskState),
                    Attempts = info.Attempts,
                    ParentTaskId = info.ParentTaskId,
                    TaskGroupLock = info.TaskGroupLock,
                };
            MTaskState mtaskState;
            if(!Enum.TryParse(info.State.ToString(), true, out mtaskState))
            {
                logger.ErrorFormat("�� ���� ��������������� TaskState(RemouteTaskQueue) � TaskState(MonitoringDataTypes). TaskId: {0}", taskMetadata.TaskId); // todo �������� ���������� ���������
                return false;
            }
            taskMetadata.State = mtaskState;
            return true;
        }

        private readonly ILog logger = LogManager.GetLogger(typeof(MonitoringTask));
        private readonly IHandleTasksMetaStorage handleTasksMetaStorage;
        private readonly IEventLogRepository eventLogRepository;
        private readonly ILocalStorage localStorage;
        private readonly IGlobalTime globalTime;
        private readonly IThreadUpdateStartingTicksCache startingTicksCache;
        private readonly IEventCache eventCache;
        private readonly ICassandraClusterSettings cassandraClusterSettings;
        private readonly object lockObject = new object();
    }
}