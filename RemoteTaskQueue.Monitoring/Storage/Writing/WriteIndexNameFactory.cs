﻿using System;

using JetBrains.Annotations;

using RemoteQueue.Cassandra.Entities;

using RemoteTaskQueue.Monitoring.Storage.Utils;

namespace RemoteTaskQueue.Monitoring.Storage.Writing
{
    public class WriteIndexNameFactory : IWriteIndexNameFactory
    {
        public WriteIndexNameFactory(IndexChecker indexChecker)
        {
            this.indexChecker = indexChecker;
        }

        [NotNull]
        public string GetIndexForTask([NotNull] TaskMetaInformation taskMetaInformation)
        {
            if(IsOldTask(taskMetaInformation))
            {
                var indexName = BuildIndexNameForTime(taskMetaInformation.Ticks, oldIndexNameFormat);
                if(!indexChecker.CheckAliasExists(indexName))
                {
                    //NOTE эта ситуация возможна если пишут сразу старую задачу в не созданный еще индекс - alias еще не создан (тк индекс еще не создали)
                    indexName = BuildIndexNameForTime(taskMetaInformation.Ticks, CurrentIndexNameFormat);
                }
                return indexName;
            }
            return BuildIndexNameForTime(taskMetaInformation.Ticks, CurrentIndexNameFormat);
        }

        private static bool IsOldTask([NotNull] TaskMetaInformation taskMetaInformation)
        {
            return taskMetaInformation.LastModificationTicks.Value > OldTaskInterval.Ticks + taskMetaInformation.Ticks;
        }

        [NotNull]
        public static string BuildIndexNameForTime(long ticksUtc, [NotNull] string format)
        {
            return DateTimeFormatter.DateFromTicks(ticksUtc).ToString(format);
        }

        private readonly IndexChecker indexChecker;
        public static readonly TimeSpan OldTaskInterval = TimeSpan.FromDays(1);
        public static readonly string CurrentIndexNameFormat = IndexNameConverter.ConvertToDateTimeFormat(RtqElasticsearchConsts.CurrentIndexNameFormat);
        private static readonly string oldIndexNameFormat = IndexNameConverter.ConvertToDateTimeFormat(IndexNameConverter.FillIndexNamePlaceholder(RtqElasticsearchConsts.OldDataAliasFormat, RtqElasticsearchConsts.CurrentIndexNameFormat));
    }
}