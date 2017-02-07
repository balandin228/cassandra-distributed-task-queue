using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using NUnit.Framework;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories.Indexes;
using RemoteQueue.Handling;

using RemoteTaskQueue.FunctionalTests.Common.ConsumerStateImpl;
using RemoteTaskQueue.FunctionalTests.Common.TaskDatas;

using SKBKontur.Catalogue.NUnit.Extensions.EdiTestMachinery;
using SKBKontur.Catalogue.ServiceLib.Logging;
using SKBKontur.Catalogue.TestCore.Waiting;

namespace RemoteTaskQueue.FunctionalTests.RemoteTaskQueue.ExchangeTests
{
    public class SimpleTaskTest : ExchangeTestBase
    {
        [Test]
        [Repeat(10)]
        public void TestRun()
        {
            var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue();
            Wait(new[] {taskId}, 1);
            Thread.Sleep(2000);
            Assert.AreEqual(1, testCounterRepository.GetCounter(taskId));
            Assert.AreEqual(TaskState.Finished, remoteTaskQueue.GetTaskInfo<SimpleTaskData>(taskId).Context.State);
            CheckTaskMinimalStartTicksIndexStates(new Dictionary<string, TaskIndexShardKey>
                {
                    {taskId, TaskIndexShardKey("SimpleTaskData", TaskState.Finished)}
                });
        }

        [Test]
        public void TestRunMultipleTasks()
        {
            var taskIds = new List<string>();
            Enumerable.Range(0, 42).AsParallel().ForAll(x =>
                {
                    var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue();
                    lock(taskIds)
                        taskIds.Add(taskId);
                });
            WaitForTasksToFinish(taskIds, TimeSpan.FromSeconds(10));
            CheckTaskMinimalStartTicksIndexStates(taskIds.ToDictionary(s => s, s => TaskIndexShardKey("SimpleTaskData", TaskState.Finished)));
        }

        [Test]
        public void TestCancel()
        {
            var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue(TimeSpan.FromSeconds(1));
            Assert.That(remoteTaskQueue.TryCancelTask(taskId), Is.EqualTo(TaskManipulationResult.Success));
            Wait(new[] {taskId}, 0);
            Thread.Sleep(2000);
            Assert.AreEqual(0, testCounterRepository.GetCounter(taskId));
            Assert.AreEqual(TaskState.Canceled, remoteTaskQueue.GetTaskInfo<SimpleTaskData>(taskId).Context.State);
            CheckTaskMinimalStartTicksIndexStates(new Dictionary<string, TaskIndexShardKey>
                {
                    {taskId, TaskIndexShardKey("SimpleTaskData", TaskState.Canceled)}
                });
        }

        [Test]
        public void TestCancel_UnknownTask()
        {
            Assert.That(remoteTaskQueue.TryCancelTask(Guid.NewGuid().ToString()), Is.EqualTo(TaskManipulationResult.Failure_TaskDoesNotExist));
        }

        [Test]
        public void TestCancel_LockAcquiringFails()
        {
            var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue(TimeSpan.FromSeconds(5));
            var remoteLockCreator = remoteTaskQueue.RemoteLockCreator;
            using(remoteLockCreator.Lock(taskId))
                Assert.That(remoteTaskQueue.TryCancelTask(taskId), Is.EqualTo(TaskManipulationResult.Failure_LockAcquiringFails));
        }

        [Test]
        public void TestRerun()
        {
            var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue();
            Wait(new[] {taskId}, 1);
            Assert.That(remoteTaskQueue.TryRerunTask(taskId, TimeSpan.FromMilliseconds(1)), Is.EqualTo(TaskManipulationResult.Success));
            Wait(new[] {taskId}, 2);
            Thread.Sleep(2000);
            Assert.AreEqual(2, testCounterRepository.GetCounter(taskId));
            var taskMeta = remoteTaskQueue.GetTaskInfo<SimpleTaskData>(taskId).Context;
            Assert.AreEqual(TaskState.Finished, taskMeta.State);
            Assert.AreEqual(2, taskMeta.Attempts);
            CheckTaskMinimalStartTicksIndexStates(new Dictionary<string, TaskIndexShardKey>
                {
                    {taskId, TaskIndexShardKey("SimpleTaskData", TaskState.Finished)}
                });
        }

        [Test]
        public void TestRerun_UnknownTask()
        {
            Assert.That(remoteTaskQueue.TryRerunTask(Guid.NewGuid().ToString(), TimeSpan.Zero), Is.EqualTo(TaskManipulationResult.Failure_TaskDoesNotExist));
        }

        [Test]
        public void TestRerun_LockAcquiringFails()
        {
            var taskId = remoteTaskQueue.CreateTask(new SimpleTaskData()).Queue(TimeSpan.FromSeconds(5));
            using(remoteTaskQueue.RemoteLockCreator.Lock(taskId))
                Assert.That(remoteTaskQueue.TryRerunTask(taskId, TimeSpan.Zero), Is.EqualTo(TaskManipulationResult.Failure_LockAcquiringFails));
        }

        private void Wait(string[] taskIds, int criticalValue, int ms = 5000)
        {
            var current = 0;
            while(true)
            {
                var attempts = taskIds.Select(testCounterRepository.GetCounter).ToArray();
                Log.For(this).Info(Now() + " CurrentValues: " + string.Join(", ", attempts));
                var minValue = attempts.Min();
                if(minValue >= criticalValue)
                    break;
                Thread.Sleep(sleepInterval);
                current += sleepInterval;
                if(current > ms)
                    throw new TooLateException("����� �������� ��������� {0} ��.", ms);
            }
        }

        private void WaitForTasksToFinish(IEnumerable<string> taskIds, TimeSpan timeSpan)
        {
            WaitHelper.Wait(() =>
                {
                    var tasks = remoteTaskQueue.HandleTaskCollection.GetTasks(taskIds.ToArray());
                    return tasks.All(t => t.Meta.State == TaskState.Finished) ? WaitResult.StopWaiting : WaitResult.ContinueWaiting;
                }, timeSpan);
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("dd.MM.yyyy mm:hh:ss.ffff");
        }

        private const int sleepInterval = 200;

        [Injected]
        private ITestCounterRepository testCounterRepository;
    }
}