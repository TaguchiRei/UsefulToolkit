using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Ai
{
    public class ReviewManager
    {
        private static ReviewManager instance;
        public static ReviewManager Instance => instance ??= new ReviewManager();

        [Serializable]
        public class ReviewRequest
        {
            public string id;
            public string requester;
            public string reviewer;
            public string changeDescription;
            public string pendingContent; 
            public string targetPath;      
            public bool isApproved;
        }

        private Dictionary<string, ReviewRequest> reviews = new();

        public string CreateReview(string requester, string reviewer, string description, string content, string target)
        {
            string id = "REV-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            var request = new ReviewRequest
            {
                id = id,
                requester = requester,
                reviewer = reviewer,
                changeDescription = description,
                pendingContent = content,
                targetPath = target,
                isApproved = false
            };
            reviews[id] = request;
            return id;
        }

        public ReviewRequest GetReview(string id)
        {
            reviews.TryGetValue(id, out var request);
            return request;
        }

        public void Approve(string id)
        {
            if (reviews.TryGetValue(id, out var request))
            {
                request.isApproved = true;
                ApplyChange(request);
            }
        }

        public void Reject(string id)
        {
            if (reviews.TryGetValue(id, out var request))
            {
                // 必要であればここでステータス管理を行う（現在はDictionaryから消さない運用）
            }
        }

        public List<ReviewRequest> GetActiveReviews()
        {
            return reviews.Values.Where(r => !r.isApproved).ToList();
        }

        private void ApplyChange(ReviewRequest request)
        {
            if (System.IO.File.Exists(request.targetPath))
            {
                System.IO.File.WriteAllText(request.targetPath, request.pendingContent);
                UnityEditor.AssetDatabase.Refresh();
                Debug.Log($"[ReviewSystem] Automatically applied changes for {request.id} to {request.targetPath}");
            }
        }
    }

    [Serializable]
    public class RequestOtherCommand : IContactCommand
    {
        public string Description => "他のエージェントにタスクを依頼します。引数(配列): [\"targetAgentName\", \"message\"]。例: [\"作業者\", \"テトリスの基盤を作成して\"]";

        public string Execute(string targetAgentName, string message)
        {
            var agent = FindAgentByName(targetAgentName);
            if (agent == null) return $"Error: Target agent '{targetAgentName}' not found.";

            // 修正: タスクを発行するように変更
            string taskId = TaskManager.Instance.CreateTask("設計者", targetAgentName, message);
            if (string.IsNullOrEmpty(taskId))
            {
                return $"Error: {targetAgentName} has an incomplete task. You can only assign one task at a time.";
            }

            AiChatSessionManager.AddMessage(agent, new ChatMessage { Role = "user", Content = $"[New Task Issued]\nTask ID: {taskId}\nDescription: {message}" });
            
            return $"Task requested and issued. Sent to {agent.Name}. Task ID: {taskId}. Task: {message}";
        }

        private AgentData FindAgentByName(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:AgentData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null && agent.Name == name) return agent;
            }
            return null;
        }
    }

    public class TaskManager
    {
        private static TaskManager instance;
        public static TaskManager Instance => instance ??= new TaskManager();

        [Serializable]
        public class TaskRequest
        {
            public string id;
            public string requester;
            public string worker;
            public string description;
            public bool isCompleted;
            public string result;
        }

        private Dictionary<string, TaskRequest> tasks = new();

        public string CreateTask(string requester, string worker, string description)
        {
            // 同じWorkerに対する未完了タスクの数をチェック
            int incompleteCount = 0;
            foreach (var t in tasks.Values)
            {
                if (t.worker == worker && !t.isCompleted)
                {
                    incompleteCount++;
                }
            }

            if (incompleteCount > 0) return null; // 排他制御

            string id = "TASK-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            var task = new TaskRequest
            {
                id = id,
                requester = requester,
                worker = worker,
                description = description,
                isCompleted = false
            };
            tasks[id] = task;
            return id;
        }

        public TaskRequest GetTask(string id)
        {
            tasks.TryGetValue(id, out var task);
            return task;
        }

        public List<TaskRequest> GetActiveTasks()
        {
            return tasks.Values.Where(t => !t.isCompleted).ToList();
        }

        public void CompleteTask(string id, string result)
        {
            if (tasks.TryGetValue(id, out var task))
            {
                task.isCompleted = true;
                task.result = result;
            }
        }

        public bool HasIncompleteTask(string worker)
        {
            return tasks.Values.Any(t => t.worker == worker && !t.isCompleted);
        }
    }

    [Serializable]
    public class RequestCreateCommand : IContactCommand
    {
        public string Description => "他のエージェントに実装タスクを発行します。引数(配列): [\"targetAgentName\", \"description\"]。※既に作業中のタスクがある場合は発行できません。";

        public string Execute(string targetAgentName, string message)
        {
            var agent = FindAgentByName(targetAgentName);
            if (agent == null) return $"Error: Target agent '{targetAgentName}' not found.";

            if (TaskManager.Instance.HasIncompleteTask(targetAgentName))
            {
                return $"Error: {targetAgentName} has an incomplete task. You can only assign one task at a time. Please wait for the completion report.";
            }

            string taskId = TaskManager.Instance.CreateTask("設計者", targetAgentName, message);

            AiChatSessionManager.AddMessage(agent, new ChatMessage { Role = "user", Content = $"[New Task Issued]\nTask ID: {taskId}\nDescription: {message}" });
            
            return $"Task issued to {agent.Name}. Task ID: {taskId}. Description: {message}";
        }

        private AgentData FindAgentByName(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:AgentData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null && agent.Name == name) return agent;
            }
            return null;
        }
    }

    [Serializable]
    public class ReportTaskCompletedCommand : IContactCommand
    {
        public string Description => "タスクの完了を報告します。引数(配列): [\"taskId\", \"result\"]。例: [\"TASK-12345\", \"TetrisBoard.csの実装が完了しました\"]";

        public string Execute(string taskId, string result)
        {
            var task = TaskManager.Instance.GetTask(taskId);
            if (task == null) return "Error: Invalid TaskID.";

            TaskManager.Instance.CompleteTask(taskId, result);

            var requesterAgent = FindAgentByName(task.requester);
            if (requesterAgent != null)
            {
                AiChatSessionManager.AddMessage(requesterAgent, new ChatMessage { Role = "user", Content = $"[Task Completed]\nTask ID: {taskId}\nResult: {result}" });
            }

            return $"Task {taskId} marked as completed and reported to {task.requester}.";
        }

        private AgentData FindAgentByName(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:AgentData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null && agent.Name == name) return agent;
            }
            return null;
        }
    }

    [Serializable]
    public class RequestReviewCommand : IContactCommand
    {
        public string Description => "他のエージェントにレビューを依頼します。引数(配列): [\"targetAgentName\", \"message\"]。messageは\"{description}\\n{content}\\n{targetPath}\"形式。";

        public string Execute(string targetAgentName, string message)
        {
            var agent = FindAgentByName(targetAgentName);
            if (agent == null) return $"Error: Target agent '{targetAgentName}' not found.";

            // メッセージをパースしてReviewRequestを作成
            string[] lines = message.Split(new[] { '\n' }, 3);
            string desc = lines.Length > 0 ? lines[0] : "No description";
            string content = lines.Length > 1 ? lines[1] : "";
            string path = lines.Length > 2 ? lines[2] : "";

            if (AIBlackList.Instance.IsBlacklisted(path))
                return $"Error: Access denied. Target path '{path}' is blacklisted.";

            string reviewId = ReviewManager.Instance.CreateReview("設計者", targetAgentName, desc, content, path);

            AiChatSessionManager.AddMessage(agent, new ChatMessage { Role = "user", Content = $"[Review Request]\nReview ID: {reviewId}\nDescription: {desc}\nTarget Path: {path}\n\nContent:\n{content}" });
            
            return $"Review requested. Sent to {agent.Name}. Review ID: {reviewId}";
        }

        private AgentData FindAgentByName(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:AgentData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null && agent.Name == name) return agent;
            }
            return null;
        }
    }

    [Serializable]
    public class RejectContentCommand : IGetCommand
    {
        public string Description => "レビューを拒否します。引数(配列): [\"reviewId\", \"feedback\"]。例: [\"REV-12345\", \"もう少し修正が必要です\"]";

        public string Execute(string argument)
        {
            string[] args;
            try { args = JsonConvert.DeserializeObject<string[]>(argument); }
            catch { return "Error: Invalid argument format. Expected [\"reviewId\", \"feedback\"]."; }

            if (args == null || args.Length < 2) return "Error: Expected 2 arguments: [reviewId, feedback].";

            var review = ReviewManager.Instance.GetReview(args[0]);
            if (review == null) return "Error: Invalid ReviewID.";

            ReviewManager.Instance.Reject(args[0]);

            var requesterAgent = FindAgentByName(review.requester);
            if (requesterAgent != null)
            {
                AiChatSessionManager.AddMessage(requesterAgent, new ChatMessage { Role = "user", Content = $"[Review Rejected]\nReview ID: {args[0]}\nFeedback: {args[1]}" });
            }

            return $"Review {args[0]} REJECTED. Feedback: {args[1]}. Notified {review.requester}.";
        }

        private AgentData FindAgentByName(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:AgentData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentData>(path);
                if (agent != null && agent.Name == name) return agent;
            }
            return null;
        }
    }

    [Serializable]
    public class ApproveContentCommand : IGetCommand
    {
        public string Description => "レビューを承認します。引数(配列): [\"reviewId\"]。例: [\"REV-12345\"]";

        public string Execute(string argument)
        {
            string[] args;
            try { args = JsonConvert.DeserializeObject<string[]>(argument); }
            catch { return "Error: Invalid argument format. Expected [\"reviewId\"]."; }

            if (args == null || args.Length < 1) return "Error: Expected 1 argument: [reviewId].";

            var review = ReviewManager.Instance.GetReview(args[0]);
            if (review == null) return "Error: Invalid ReviewID.";

            if (AIBlackList.Instance.IsBlacklisted(review.targetPath))
                return $"Error: Access denied. Target path '{review.targetPath}' is blacklisted.";

            ReviewManager.Instance.Approve(args[0]);
            return $"Review {args[0]} APPROVED. System has applied the changes.";
        }
    }

    [Serializable]
    public class ListTasksCommand : IGetCommand
    {
        public string Description => "現在進行中のタスク一覧を取得します。引数(配列): []";

        public string Execute(string argument)
        {
            var tasks = TaskManager.Instance.GetActiveTasks();
            if (tasks.Count == 0) return "No active tasks.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Active Tasks:");
            foreach (var t in tasks)
            {
                sb.AppendLine($"- ID: {t.id}, Worker: {t.worker}, Description: {t.description}");
            }
            return sb.ToString();
        }
    }

    [Serializable]
    public class ListReviewsCommand : IGetCommand
    {
        public string Description => "未完了のレビュー一覧を取得します。引数(配列): []";

        public string Execute(string argument)
        {
            var reviews = ReviewManager.Instance.GetActiveReviews();
            if (reviews.Count == 0) return "No active reviews.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Active Reviews:");
            foreach (var r in reviews)
            {
                sb.AppendLine($"- ID: {r.id}, Reviewer: {r.reviewer}, Target: {r.targetPath}");
            }
            return sb.ToString();
        }
    }
}
