using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Hangfire.Mutex
{
    public sealed class MutexAttribute : JobFilterAttribute, IElectStateFilter, IApplyStateFilter
    {
        private static readonly TimeSpan DistributedLockTimeout = TimeSpan.FromMinutes(1);

        private readonly string _resource;

        public MutexAttribute(string resource)
        {
            _resource = resource;
        }

        public void OnStateElection(ElectStateContext context)
        {
            if (context.CandidateState.Name != EnqueuedState.StateName ||
                context.BackgroundJob.Job == null)
            {
                return;
            }

            // This filter requires an extended set of storage operations. It's supported
            // by all the official storages, and many of the community-based ones.
            var storageConnection = context.Connection as JobStorageConnection;
            if (storageConnection == null)
            {
                throw new NotSupportedException(
                    "This version of storage doesn't support extended methods. Please try to update to the latest version.");
            }

            try
            {
                using (AcquireDistributedSetLock(context.Connection, context.BackgroundJob.Job.Args))
                {
                    var range = storageConnection.GetRangeFromSet(
                        GetResourceKey(context.BackgroundJob.Job.Args),
                        0,
                        0);

                    var blockedBy = range.Count > 0 ? range[0] : null;

                    if (blockedBy == null || blockedBy == context.BackgroundJob.Id)
                    {
                        var localTransaction = context.Connection.CreateWriteTransaction();

                        localTransaction.AddToSet(GetResourceKey(context.BackgroundJob.Job.Args),
                            context.BackgroundJob.Id);
                        localTransaction.Commit();

                        return;
                    }
                }
            }
            catch (DistributedLockTimeoutException)
            {
            }

            context.CandidateState = CreateDeletedState();
        }

        public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
        {
            if (context.BackgroundJob.Job == null) return;

            if (context.NewState.Name == DeletedState.StateName
                || context.NewState.Name == SucceededState.StateName
                || context.NewState.Name == FailedState.StateName
               )
            {
                using (AcquireDistributedSetLock(context.Connection, context.BackgroundJob.Job.Args))
                {
                    var localTransaction = context.Connection.CreateWriteTransaction();
                    localTransaction.RemoveFromSet(GetResourceKey(context.BackgroundJob.Job.Args),
                        context.BackgroundJob.Id);

                    localTransaction.Commit();
                }
            }
        }

        public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
        {
        }

        private static DeletedState CreateDeletedState(string? blockedBy = null)
        {
            return new DeletedState
            {
                Reason = $"Execution was blocked by background job {blockedBy}"
            };
        }

        private IDisposable AcquireDistributedSetLock(IStorageConnection connection, IReadOnlyList<object> args)
        {
            return connection.AcquireDistributedLock(GetDistributedLockKey(args), DistributedLockTimeout);
        }

        private string GetDistributedLockKey(IReadOnlyList<object> args)
        {
            return $"extension:job-mutex:lock:{GetKeyFormat(_resource, args)}";
        }

        private string GetResourceKey(IReadOnlyList<object> args)
        {
            return $"extension:job-mutex:set:{GetKeyFormat(_resource, args)}";
        }

        private static string GetKeyFormat(string keyFormat, IReadOnlyList<object> args)
        {
            return string.Format(keyFormat, args.ToArray());
        }
    }
}