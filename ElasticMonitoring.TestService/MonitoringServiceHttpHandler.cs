﻿using System;

using Elasticsearch.Net;

using RemoteTaskQueue.Monitoring.Indexer;
using RemoteTaskQueue.Monitoring.Storage;
using RemoteTaskQueue.Monitoring.TaskCounter;

using SKBKontur.Catalogue.Core.ElasticsearchClientExtensions;
using SKBKontur.Catalogue.Core.EventFeeds;
using SKBKontur.Catalogue.Objects;
using SKBKontur.Catalogue.ServiceLib.HttpHandlers;

namespace SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TestService
{
    public class MonitoringServiceHttpHandler : IHttpHandler
    {
        public MonitoringServiceHttpHandler(RtqTaskCounterEventFeeder taskCounterEventFeeder,
                                            RtqMonitoringEventFeeder monitoringEventFeeder,
                                            RtqElasticsearchSchema rtqElasticsearchSchema,
                                            Lazy<IRtqElasticsearchClient> elasticClient)
        {
            this.taskCounterEventFeeder = taskCounterEventFeeder;
            this.monitoringEventFeeder = monitoringEventFeeder;
            this.rtqElasticsearchSchema = rtqElasticsearchSchema;
            this.elasticClient = elasticClient;
        }

        [HttpMethod]
        public RtqTaskCounters GetTaskCounters()
        {
            return taskCounterStateManager.GetTaskCounters(Timestamp.Now);
        }

        [HttpMethod]
        public void Stop()
        {
            StopFeeding();
        }

        [HttpMethod]
        public void ExecuteForcedFeeding()
        {
            monitoringFeedsRunner.ResetLocalState();
            monitoringFeedsRunner.ExecuteForcedFeeding(delayUpperBound : TimeSpan.MaxValue);
            elasticClient.Value.IndicesRefresh<StringResponse>("_all").EnsureSuccess();

            taskCounterFeedsRunner.ExecuteForcedFeeding(delayUpperBound : TimeSpan.MaxValue);
        }

        [HttpMethod]
        public void ResetState()
        {
            StopFeeding();

            DeleteAllElasticEntities();
            rtqElasticsearchSchema.Actualize(local : true, bulkLoad : false);
            monitoringEventFeeder.GlobalTime.ResetInMemoryState();
            taskCounterEventFeeder.GlobalTime.ResetInMemoryState();

            monitoringFeedsRunner = monitoringEventFeeder.RunEventFeeding();
            (taskCounterFeedsRunner, taskCounterStateManager, _) = taskCounterEventFeeder.RunEventFeeding();
        }

        private void StopFeeding()
        {
            StopFeeding(ref taskCounterFeedsRunner);
            StopFeeding(ref monitoringFeedsRunner);
        }

        private static void StopFeeding(ref IEventFeedsRunner feedsRunner)
        {
            if (feedsRunner != null)
            {
                feedsRunner.Stop();
                feedsRunner = null;
            }
        }

        private void DeleteAllElasticEntities()
        {
            elasticClient.Value.IndicesDelete<StringResponse>(RtqElasticsearchConsts.AllIndicesWildcard, new DeleteIndexRequestParameters {RequestConfiguration = allowNotFoundStatusCode}).EnsureSuccess();
            elasticClient.Value.IndicesDeleteTemplateForAll<StringResponse>(RtqElasticsearchConsts.TemplateName, new DeleteIndexTemplateRequestParameters {RequestConfiguration = allowNotFoundStatusCode}).EnsureSuccess();
            elasticClient.Value.ClusterHealth<StringResponse>(new ClusterHealthRequestParameters {WaitForStatus = WaitForStatus.Green}).EnsureSuccess();
        }

        private IEventFeedsRunner monitoringFeedsRunner;
        private IEventFeedsRunner taskCounterFeedsRunner;
        private RtqTaskCounterStateManager taskCounterStateManager;
        private readonly RtqTaskCounterEventFeeder taskCounterEventFeeder;
        private readonly RtqMonitoringEventFeeder monitoringEventFeeder;
        private readonly RtqElasticsearchSchema rtqElasticsearchSchema;
        private readonly Lazy<IRtqElasticsearchClient> elasticClient;
        private readonly RequestConfiguration allowNotFoundStatusCode = new RequestConfiguration {AllowedStatusCodes = new[] {404}};
    }
}