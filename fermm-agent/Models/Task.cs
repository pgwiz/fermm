using System;
using System.Text.Json.Serialization;

namespace FermmAgent.Models
{
    public enum TaskStatus 
    { 
        Pending, 
        Running, 
        Completed, 
        Failed 
    }

    public enum TaskType 
    { 
        Shell, 
        Upload, 
        Download, 
        Execute, 
        Screenshot 
    }

    public class AgentTask
    {
        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("payload")]
        public string Payload { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("retries")]
        public int Retries { get; set; } = 0;

        [JsonPropertyName("max_retries")]
        public int MaxRetries { get; set; } = 3;
    }
}
