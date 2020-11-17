using System.Threading.Tasks;

using GroBuf;

using JetBrains.Annotations;

using Task = SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Entities.Task;

namespace SkbKontur.Cassandra.DistributedTaskQueue.Handling
{
    [PublicAPI]
    public interface IRtqTaskHandler
    {
        [NotNull]
        Task<HandleResult> HandleTaskAsync([NotNull] IRtqTaskProducer taskProducer, [NotNull] ISerializer serializer, [NotNull] Task task);
    }
}