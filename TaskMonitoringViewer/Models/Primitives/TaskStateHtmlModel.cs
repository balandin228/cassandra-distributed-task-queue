﻿using System.Web.Mvc;

using SKBKontur.Catalogue.Core.Web.Models.HtmlModels;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities.Primitives;

namespace SKBKontur.Catalogue.RemoteTaskQueue.TaskMonitoringViewer.Models.Primitives
{
    public class TaskStateHtmlModel : IHtmlModel
    {
        public TaskStateHtmlModel(string taskId)
        {
            this.taskId = taskId;
        }

        public string Id { get; set; }

        public string GetCancelUrl(UrlHelper url)
        {
            return url.GetCancelTaskUrl(taskId);
        }

        public string GetRerunUrl(UrlHelper url)
        {
            return url.GetRerunTaskUrl(taskId);
        }

        public TaskState Value { get; set; }
        public bool CanCancel { get { return Value == TaskState.New || Value == TaskState.WaitingForRerun || Value == TaskState.WaitingForRerunAfterError; } }

        private readonly string taskId;
    }
}