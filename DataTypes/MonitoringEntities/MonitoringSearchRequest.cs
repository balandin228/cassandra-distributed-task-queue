﻿using SKBKontur.Catalogue.Core.CommonBusinessObjects;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities.Primitives;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities
{
    public class MonitoringSearchRequest : BusinessObject
    {
        public TaskState[] States { get; set; }
        public string Name { get; set; }
        public string TaskId { get; set; }
        public string ParentTaskId { get; set; }

        public DateTimeRange Ticks { get; set; }
        public DateTimeRange MinimalStartTicks { get; set; }
        public DateTimeRange StartExecutingTicks { get; set; }
    }
}