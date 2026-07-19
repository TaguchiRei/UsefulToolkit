using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    public static class AgentDataGenerator
    {
        [MenuItem("UsefulToolkit/AI/Generate Default Agents", false, 12)]
        public static void Generate()
        {
            CreateReviewer();
            CreateWorker();
            CreateDesigner();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Default Agents generated successfully.");
        }

        private static void CreateReviewer()
        {
            var data = FileGenerator.AutoGenerateAsset<AgentData>("ReviewerAgent", GenerateType.Editor, "Agents/Data");
            data.Name = "レビュアー";
            data.Role = "コードや設計の品質を検証し、フィードバックを提供します。";
            data.SystemPrompt =
                "あなたはシニアエンジニアのレビュアーです。提出された変更内容（ReviewID）を詳細に調査し、プロジェクトの標準、パフォーマンス、可読性の観点からレビューしてください。問題がなければ ApproveContentCommand で承認し、修正が必要な場合は RejectContentCommand でフィードバックを返してください。";

            var getCommands = new List<IGetCommand>
            {
                new ReadFileCommand(),
                new ReadFolderCommand(),
                new GetFilePathCommand(),
                new GetFolderPathCommand(),
                new GetComponentDataCommand(),
                new GetSceneStructureCommand(),
                new GetChildrenCommand(),
                new GetSceneObjectCommand(),
                new GetAssetDataCommand(),
                new GetConsoleLogsCommand(),
                new ApproveContentCommand(),
                new RejectContentCommand()
            };

            var contactCommands = new List<IContactCommand>
            {
                new RequestOtherCommand()
            };

            SetPrivateFields(data, getCommands.ToArray(), new ISetCommand[0], contactCommands.ToArray());
            EditorUtility.SetDirty(data);
        }

        private static void CreateWorker()
        {
            var data = FileGenerator.AutoGenerateAsset<AgentData>("WorkerAgent", GenerateType.Editor, "Agents/Data");
            data.Name = "作業者";
            data.Role = "具体的な実装、バグ修正、シーンの編集を行います。";
            data.SystemPrompt =
                "あなたは熟練したプログラマーです。指示されたタスクに基づき、ソースコードの作成・修正やUnityシーンの編集を正確に行います。\n" +
                "タスクが完了したら、ReportTaskCompletedCommandを使用してタスクIDと完了報告を設計者に送ってください。その後、RequestReviewCommandでレビュアーにチェックを依頼してください。";

            var getCommands = new List<IGetCommand>
            {
                new ReadFileCommand(),
                new ReadFolderCommand(),
                new GetFilePathCommand(),
                new GetFolderPathCommand(),
                new GetComponentDataCommand(),
                new GetSceneStructureCommand(),
                new GetChildrenCommand(),
                new GetSceneObjectCommand(),
                new GetAssetDataCommand(),
                new GetConsoleLogsCommand()
            };

            var setCommands = new List<ISetCommand>
            {
                new CreateFileCommand(),
                new CreateFolderCommand(),
                new ChangeFileCommand(),
                new ChangeInspectorCommand(),
                new ModifyAssetCommand(),
                new CreateScriptableObjectCommand(),
                new CreatePrefabCommand(),
                new ApplyPrefabOverridesCommand(),
                new ExecuteGeneratorCommand()
            };

            var contactCommands = new List<IContactCommand>
            {
                new RequestReviewCommand(),
                new ReportTaskCompletedCommand()
            };

            SetPrivateFields(data, getCommands.ToArray(), setCommands.ToArray(), contactCommands.ToArray());
            EditorUtility.SetDirty(data);
        }

        private static void CreateDesigner()
        {
            var data = FileGenerator.AutoGenerateAsset<AgentData>("DesignerAgent", GenerateType.Editor, "Agents/Data");
            data.Name = "設計者";
            data.Role = "システム全体の設計を行い、他のエージェントにタスクを割り振ります。";
            data.SystemPrompt =
                "あなたはソフトウェアアーキテクトです。ユーザーの要望を分析し、Clean Architectureに基づいた最適なシステム構成を設計します。\n" +
                "作業手順:\n" +
                "1. 要件を分析し、必要なコンポーネントとクラスの構造を検討してください。\n" +
                "2. 決定したクラス構造を作業者にRequestCreateCommandで依頼してください。\n" +
                "3. 【重要】依頼後は勝手にファイルシステムや進捗確認（ReadFolderCommand等）を行わず、必ず作業者からの完了報告（ReportTaskCompletedCommand）を待機してください。\n" +
                "4. 完了報告を受け取ったら、結果を検証し、レビュアーにRequestReviewCommandを依頼してください。\n" +
                "自分ですべてを実装するのではなく、作業者やレビュアーと連携し、設計意図を確実に実装へ落とし込んでください。";

            var getCommands = new List<IGetCommand>
            {
                new ReadFileCommand(),
                new ReadFolderCommand(),
                new GetFilePathCommand(),
                new GetFolderPathCommand(),
                new GetComponentDataCommand(),
                new GetSceneStructureCommand(),
                new GetChildrenCommand(),
                new GetSceneObjectCommand(),
                new GetAssetDataCommand()
            };

            var setCommands = new List<ISetCommand>
            {
                new CreateFolderCommand()
            };

            var contactCommands = new List<IContactCommand>
            {
                new RequestOtherCommand(),
                new RequestCreateCommand()
            };

            SetPrivateFields(data, getCommands.ToArray(), setCommands.ToArray(), contactCommands.ToArray());
            EditorUtility.SetDirty(data);
        }

        private static void SetPrivateFields(AgentData data, IGetCommand[] getCommands, ISetCommand[] setCommands,
            IContactCommand[] contactCommands)
        {
            var type = typeof(AgentData);
            var getField = type.GetField("_getCommands", BindingFlags.NonPublic | BindingFlags.Instance);
            var setField = type.GetField("_setCommands", BindingFlags.NonPublic | BindingFlags.Instance);
            var contactField = type.GetField("_contactCommands", BindingFlags.NonPublic | BindingFlags.Instance);

            if (getField != null) getField.SetValue(data, getCommands);
            if (setField != null) setField.SetValue(data, setCommands);
            if (contactField != null) contactField.SetValue(data, contactCommands);
        }
    }
}