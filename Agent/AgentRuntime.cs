using AIToolkit.Files;
using AIToolkit.LLM;

namespace AIToolkit.Agent
{
    public interface IAgentRuntime
    {
        void Start();
        void Stop();
    }

    public sealed class AgentRuntime : IAgentRuntime, IDisposable
    {
        public static AgentRuntime Instance { get; } = new();
        public static AgentConfig Config => Instance._config;

        private readonly CancellationTokenSource _cts = new();
        private readonly List<IAgentPlugin> _plugins = [];
        private Task? _loop;
        private readonly Lock _queueLock = new();
        private AgentConfig _config = null!;
        private AgentState _state = null!;
        private DateTime _lastUserMessageUtc = DateTime.UtcNow;
        private readonly string _configPath = "data/agent/config.json";
        private readonly string _statePath = "data/agent/state.json";
        private volatile bool _running;

        private AgentRuntime() { }

        public void Start()
        {
            if (_running) return;
            _config = AgentConfig.Load(_configPath);
            _state = AgentState.Load(_statePath);
            LoadPlugins();
            HookCoreEvents();
            _running = true;
            _loop = Task.Run(LoopAsync);
        }

        public void Stop()
        {
            _cts.Cancel();
            _running = false;
            try { _loop?.Wait(1500); } catch { }
            Persist();
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }

        private void LoadPlugins()
        {
            _plugins.Clear();
            // TODO: More Plugins go here!
            foreach (var id in _config.Plugins.Distinct())
            {
                switch (id)
                {
                    case "CoreReflection":
                        _plugins.Add(new Plugins.CoreReflectionPlugin());
                        break;
                    case "WebIntelligence":
                        _plugins.Add(new Plugins.WebIntelligencePlugin());
                        break;
                }
            }
        }

        private void HookCoreEvents()
        {
            EventBus.Subscribe<MessageAddedEvent>(e =>
            {
                if (e.IsUser) _lastUserMessageUtc = DateTime.UtcNow;
            });
            EventBus.Subscribe<SessionArchivedEvent>(_ => Enqueue(new AgentTask
            {
                Type = AgentTaskType.Observe,
                Priority = 0
            }));
            EventBus.Subscribe<BotChangedEvent>(_ => Enqueue(new AgentTask
            {
                Type = AgentTaskType.Observe,
                Priority = 0
            }));
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (!_config.Enabled || !LLMSystem.Settings.AgentEnabled)
                    {
                        await Task.Delay(2000, _cts.Token);
                        continue;
                    }

                    _state.ResetBudgetsIfNeeded();

                    await RunObservationPhase(_cts.Token);

                    var next = DequeueRunnable();
                    if (next != null)
                        await ExecuteTask(next, _cts.Token);

                    CleanupExpiredStaged();
                    Persist();

                    await Task.Delay(_config.LoopIntervalMs, _cts.Token);
                }
                catch (TaskCanceledException) { }
                catch
                {
                    // swallow
                }
            }
        }

        private async Task RunObservationPhase(CancellationToken ct)
        {
            if (IdleMinutes < _config.MinIdleMinutesBeforeBackgroundWork)
                return;

            var ctx = BuildContext();
            foreach (var plugin in _plugins)
            {
                var tasks = await plugin.ObserveAsync(ctx, ct);
                foreach (var t in tasks) Enqueue(t);
            }
        }

        private double IdleMinutes => (DateTime.UtcNow - _lastUserMessageUtc).TotalMinutes;

        private IAgentContext BuildContext()
        {
            var history = LLM.LLMSystem.History;
            return new AgentContext
            {
                SessionCount = history.Sessions.Count,
                LastUserMessage = history.GetLastUserMessageContent(),
                IdleTime = TimeSpan.FromMinutes(IdleMinutes),
                UtcNow = DateTime.UtcNow,
                Config = _config,
                State = _state
            };
        }

        private void Enqueue(AgentTask task)
        {
            lock (_queueLock)
            {
                // De-duplicate by Type + CorrelationKey while task is pending
                if (!string.IsNullOrEmpty(task.CorrelationKey) &&
                    _state.Queue.Any(t =>
                        (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Running || t.Status == AgentTaskStatus.Deferred) &&
                        t.Type == task.Type &&
                        t.CorrelationKey == task.CorrelationKey))
                {
                    return;
                }

                _state.Queue.Add(task);
            }
        }

        private AgentTask? DequeueRunnable()
        {
            lock (_queueLock)
            {
                var now = DateTime.UtcNow;
                var candidate = _state.Queue
                    .Where(t => t.Status == AgentTaskStatus.Queued && t.NotBeforeUtc <= now)
                    .OrderBy(t => t.Priority)
                    .ThenBy(t => t.CreatedUtc)
                    .FirstOrDefault();
                if (candidate != null)
                    candidate.Status = AgentTaskStatus.Running;
                return candidate;
            }
        }

        private async Task ExecuteTask(AgentTask task, CancellationToken ct)
        {
            System.Diagnostics.Debug.WriteLine($"[Agent] Execute {task.Type} key={task.CorrelationKey} prio={task.Priority}");
            var ctx = BuildContext();
            var plugin = _plugins.FirstOrDefault(p => p.CanHandle(task));
            if (plugin == null)
            {
                Fail(task);
                return;
            }

            if (task.RequiresLLM && _state.TokensUsedToday >= _config.DailyTokenBudget)
            {
                Defer(task, TimeSpan.FromMinutes(30));
                return;
            }

            AgentTaskResult result;
            try
            {
                result = await plugin.ExecuteAsync(task, ctx, ct);
            }
            catch
            {
                result = AgentTaskResult.Fail();
            }

            lock (_queueLock)
            {
                if (result.Success)
                {
                    task.Status = AgentTaskStatus.Done;
                    _state.TokensUsedToday += result.TokensUsed;
                    _state.SearchesUsedToday += result.SearchesUsed;
                    foreach (var nt in result.NewTasks)
                        Enqueue(nt);
                    foreach (var sm in result.Staged)
                    {
                        _state.StagedMessages.RemoveAll(m => m.TopicKey == sm.TopicKey && !m.Delivered);
                        _state.StagedMessages.Add(sm);
                        EventBus.Publish(new StagedMessageReadyEvent(sm));
                    }
                }
                else
                {
                    task.Attempts++;
                    if (task.Attempts > 3) task.Status = AgentTaskStatus.Failed;
                    else Defer(task, TimeSpan.FromMinutes(5 * task.Attempts));
                }
            }
        }

        private void Defer(AgentTask task, TimeSpan delay)
        {
            task.Status = AgentTaskStatus.Deferred;
            task.NotBeforeUtc = DateTime.UtcNow + delay;
        }

        private void Fail(AgentTask task) => task.Status = AgentTaskStatus.Failed;

        private void CleanupExpiredStaged()
        {
            _state.StagedMessages.RemoveAll(m => m.Delivered || m.ExpireUtc < DateTime.UtcNow);
        }

        private void Persist() => (_state as IFile).SaveToFile(_statePath);
    }
}