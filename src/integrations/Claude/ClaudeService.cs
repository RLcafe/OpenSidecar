using System;
using System.IO;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // Everything the Claude card reads (decision 027): the plan's 5-hour and weekly limits from the widget's own sign-in,
    // tokens per minute and API errors from Claude Code's logs, and the session counts from Claude Code's session list
    // and the Claude app's thread records. All of it is read-only; nothing listens on a port or hooks into Claude Code.
    public sealed class ClaudeService : IDisposable
    {
        public static readonly TimeSpan PlanEvery = TimeSpan.FromMinutes(5), BackoffLimit = TimeSpan.FromMinutes(15);
        public readonly ClaudeAccount Account;
        public readonly ClaudeSessions Sessions;
        public readonly ClaudeTokens Tokens;
        public readonly ClaudeCleanup Cleanup;
        public readonly string ConfigFolder;
        private ClaudeLimits limits;
        private bool failed;
        private DateTime lastCheck = DateTime.MinValue;

        public ClaudeService(string configFolder, string signInPath, string bridgeFolder, ClaudeAccount account = null, Func<System.Collections.Generic.IEnumerable<string>> appRoots = null)
        {
            ConfigFolder = configFolder;
            Account = account ?? new ClaudeAccount(signInPath);
            Sessions = new ClaudeSessions(configFolder, appRoots);
            Tokens = new ClaudeTokens(configFolder);
            Cleanup = new ClaudeCleanup(configFolder, bridgeFolder);
            NextWait = PlanEvery;
        }

        public ClaudeLimits Limits { get { return limits; } }
        public TimeSpan NextWait { get; private set; }
        public bool BackingOff { get { return NextWait > PlanEvery; } }

        public ClaudePlanState Plan
        {
            get
            {
                if (Account.Expired) return ClaudePlanState.Expired;
                if (!Account.SignedIn) return ClaudePlanState.SignedOut;
                if (Account.NoMeters) return ClaudePlanState.NoMeters;
                if (failed) return ClaudePlanState.Failed;
                return limits == null ? ClaudePlanState.Reading : ClaudePlanState.Current;
            }
        }

        public bool PlanDue(DateTime now) { return Account.SignedIn && now - lastCheck >= NextWait; }
        public void CheckSoon() { lastCheck = DateTime.MinValue; NextWait = PlanEvery; }

        // One usage check. A 429 or 5xx doubles the wait, up to 15 minutes; a success goes back to every 5 minutes.
        // Returns whether what the tiles show changed.
        public async Task<bool> CheckPlanAsync(DateTime now)
        {
            lastCheck = now;
            var before = Plan;
            try
            {
                var next = await Account.UsageAsync(now).ConfigureAwait(false);
                failed = false; NextWait = PlanEvery;
                if (next != null) limits = next;
                else if (!Account.SignedIn) limits = null;
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                var http = e as ClaudeHttpException;
                NextWait = http != null && http.Throttled ? Backoff(NextWait) : PlanEvery;
                return before != Plan;
            }
        }
        public static TimeSpan Backoff(TimeSpan last)
        {
            var doubled = TimeSpan.FromTicks(Math.Max(PlanEvery.Ticks, last.Ticks) * 2);
            return doubled > BackoffLimit ? BackoffLimit : doubled;
        }

        public void SignedOut() { limits = null; failed = false; NextWait = PlanEvery; }

        public void Dispose() { Account.Dispose(); }
    }
}
