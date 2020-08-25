﻿using System;
using System.Linq;

using JetBrains.Annotations;

using SkbKontur.Cassandra.DistributedTaskQueue.Handling;
using SkbKontur.Cassandra.TimeBasedUuid;
using SkbKontur.EventFeeds;
using SkbKontur.Graphite.Client;

namespace SkbKontur.Cassandra.DistributedTaskQueue.Monitoring.EventFeed
{
    internal class RtqEventFeedPeriodicJobRunner : IPeriodicJobRunner
    {
        public RtqEventFeedPeriodicJobRunner(IRtqPeriodicJobRunner rtqPeriodicJobRunner, IGraphiteClient graphiteClient)
        {
            this.rtqPeriodicJobRunner = rtqPeriodicJobRunner;
            this.graphiteClient = graphiteClient;
        }

        public void RunPeriodicJobWithLeaderElection([NotNull] string jobName,
                                                     TimeSpan delayBetweenIterations,
                                                     [NotNull] Action jobAction,
                                                     [NotNull] Func<IRunningEventFeed> onTakeTheLead,
                                                     [NotNull] Func<IRunningEventFeed> onLoseTheLead)
        {
            rtqPeriodicJobRunner.RunPeriodicJobWithLeaderElection(jobName,
                                                                  delayBetweenIterations,
                                                                  jobAction,
                                                                  () =>
                                                                      {
                                                                          var runningEventFeed = onTakeTheLead();
                                                                          var lagReportingJobId = FormatLagReportingJobId(runningEventFeed.FeedKey);
                                                                          rtqPeriodicJobRunner.RunPeriodicJob(lagReportingJobId,
                                                                                                              delayBetweenIterations : TimeSpan.FromMinutes(1),
                                                                                                              () => ReportActualizationLagToGraphite(runningEventFeed));
                                                                      },
                                                                  () =>
                                                                      {
                                                                          var runningEventFeed = onLoseTheLead();
                                                                          var lagReportingJobId = FormatLagReportingJobId(runningEventFeed.FeedKey);
                                                                          rtqPeriodicJobRunner.StopPeriodicJob(lagReportingJobId);
                                                                      });
        }

        public void StopPeriodicJobWithLeaderElection([NotNull] string jobName)
        {
            rtqPeriodicJobRunner.StopPeriodicJobWithLeaderElection(jobName);
        }

        [NotNull]
        private string FormatLagReportingJobId([NotNull] string feedKey)
        {
            return $"{feedKey}-ReportActualizationLagJob";
        }

        private void ReportActualizationLagToGraphite([NotNull] IRunningEventFeed runningEventFeed)
        {
            var offsetsToReport = runningEventFeed.GetCurrentGlobalOffsetTimestamps()
                                                  .Where(t => t.CurrentGlobalOffsetTimestamp != null)
                                                  .ToArray();
            var now = Timestamp.Now;
            foreach (var t in offsetsToReport)
            {
                var graphitePath = $"{graphitePathPrefix}.{t.BladeId.BladeKey}";
                graphiteClient.Send(graphitePath, (long)(now - t.CurrentGlobalOffsetTimestamp).TotalMilliseconds, now.ToDateTime());
            }
        }

        private readonly IRtqPeriodicJobRunner rtqPeriodicJobRunner;
        private readonly IGraphiteClient graphiteClient;

        // todo (andrew, 25.08.2020): extract
        private readonly string graphitePathPrefix = $"SubSystem.EventFeeds.ActualizationLag.{Environment.MachineName}";
    }
}