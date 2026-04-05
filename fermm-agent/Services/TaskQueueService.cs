using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FermmAgent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FermmAgent.Services
{
    public class TaskQueueService : IHostedService
    {
        private readonly ILogger<TaskQueueService> _logger;
        private readonly string _queuePath;
        private readonly ConcurrentDictionary<string, AgentTask> _taskStore;
        private readonly JsonSerializerOptions _jsonOptions;

        public TaskQueueService(ILogger<TaskQueueService> logger)
        {
            _logger = logger;
            _queuePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fermm", "queue.json"
            );
            _taskStore = new ConcurrentDictionary<string, AgentTask>();
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            LoadPersistedQueue();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TaskQueueService started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TaskQueueService stopped");
            PersistQueue();
            return Task.CompletedTask;
        }

        public string Enqueue(AgentTask task)
        {
            task.TaskId = Guid.NewGuid().ToString();
            task.CreatedAt = DateTime.UtcNow;
            task.Status = "pending";

            if (_taskStore.TryAdd(task.TaskId, task))
            {
                _logger.LogInformation("Queued task: {TaskId} of type {Type}", task.TaskId, task.Type);
                PersistQueue();
                return task.TaskId;
            }

            throw new InvalidOperationException($"Failed to enqueue task {task.TaskId}");
        }

        public AgentTask? DequeueNext()
        {
            var pending = _taskStore.Values
                .Where(t => t.Status == "pending")
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefault();

            return pending;
        }

        public AgentTask? GetTask(string taskId)
        {
            _taskStore.TryGetValue(taskId, out var task);
            return task;
        }

        public void UpdateTaskStatus(string taskId, string status, string? result = null, string? error = null)
        {
            if (!_taskStore.TryGetValue(taskId, out var task))
            {
                _logger.LogWarning("Task {TaskId} not found", taskId);
                return;
            }

            var previousStatus = task.Status;
            task.Status = status;

            if (status == "running" && task.StartedAt == null)
            {
                task.StartedAt = DateTime.UtcNow;
            }

            if (status == "completed" || status == "failed")
            {
                task.CompletedAt = DateTime.UtcNow;
            }

            if (!string.IsNullOrEmpty(result))
            {
                task.Result = result;
            }

            if (!string.IsNullOrEmpty(error))
            {
                task.Error = error;
            }

            _logger.LogInformation(
                "Task {TaskId} status: {PreviousStatus} -> {Status}",
                taskId, previousStatus, status
            );

            PersistQueue();
        }

        public void IncrementRetries(string taskId)
        {
            if (_taskStore.TryGetValue(taskId, out var task))
            {
                task.Retries++;
                if (task.Retries >= task.MaxRetries)
                {
                    UpdateTaskStatus(taskId, "failed", error: "Max retries exceeded");
                }
                else
                {
                    task.Status = "pending";
                }
                PersistQueue();
            }
        }

        public int GetPendingCount() => _taskStore.Values.Count(t => t.Status == "pending");

        public int GetTotalCount() => _taskStore.Count;

        public List<AgentTask> GetAllTasks() => _taskStore.Values.ToList();

        public List<AgentTask> GetTasksByStatus(string status)
        {
            return _taskStore.Values
                .Where(t => t.Status == status)
                .OrderBy(t => t.CreatedAt)
                .ToList();
        }

        private void LoadPersistedQueue()
        {
            if (!File.Exists(_queuePath))
            {
                _logger.LogDebug("No persisted queue found at {Path}", _queuePath);
                return;
            }

            try
            {
                var json = File.ReadAllText(_queuePath);
                var tasks = JsonSerializer.Deserialize<List<AgentTask>>(json, _jsonOptions);

                if (tasks == null || tasks.Count == 0)
                {
                    _logger.LogDebug("Persisted queue is empty");
                    return;
                }

                foreach (var task in tasks)
                {
                    if (task.Status == "pending" || task.Status == "running")
                    {
                        _taskStore.TryAdd(task.TaskId, task);
                        _logger.LogInformation(
                            "Loaded persisted task: {TaskId} (status: {Status})",
                            task.TaskId, task.Status
                        );
                    }
                }

                _logger.LogInformation("Loaded {Count} persisted tasks from disk", _taskStore.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load persisted queue from {Path}", _queuePath);
            }
        }

        private void PersistQueue()
        {
            try
            {
                var tasks = _taskStore.Values.ToList();
                var json = JsonSerializer.Serialize(tasks, _jsonOptions);

                var dir = Path.GetDirectoryName(_queuePath);
                if (string.IsNullOrEmpty(dir))
                {
                    _logger.LogError("Invalid queue path directory: {Path}", _queuePath);
                    return;
                }

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_queuePath, json);
                _logger.LogDebug("Queue persisted to disk ({Count} tasks)", tasks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist queue to {Path}", _queuePath);
            }
        }
    }
}
