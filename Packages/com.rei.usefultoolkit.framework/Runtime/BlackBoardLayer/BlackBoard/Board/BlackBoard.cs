using System.Collections.Generic;
using System;
using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// ChildBoardを型ごとに登録・取得する最上位のBlackBoard本体。
    /// シーンごとに1インスタンス、InitializationのContainer/Compositerが生成・登録する。
    /// </summary>
    public sealed class BlackBoard : IBlackBoard
    {
        private readonly Dictionary<Type, ChildStateBoardBase> _stateChildBoards = new();
        private readonly Dictionary<Type, ChildEventBoardBase> _eventChildBoards = new();

        /// <summary>
        /// シーン管理システム専用のChildBoard。
        /// </summary>
        private readonly SceneBoard _sceneBoard;

        /// <exception cref="ArgumentNullException">sceneBoardがnullのときに出力</exception>
        public BlackBoard(SceneBoard sceneBoard)
        {
            _sceneBoard = sceneBoard ?? throw new ArgumentNullException(nameof(sceneBoard));
        }

        public bool TryRegisterStateBoard<T>(T childBoard) where T : ChildStateBoardBase
        {
            var type = typeof(T);
            return _stateChildBoards.TryAdd(type, childBoard);
        }

        public SceneBoard GetSceneBoard()
        {
            return _sceneBoard;
        }

        public bool TryGetStateBoard<T>(out T childBoard) where T : ChildStateBoardBase
        {
            if (_stateChildBoards.TryGetValue(typeof(T), out var raw) && raw is T typed)
            {
                childBoard = typed;
                return true;
            }

            childBoard = null;
            return false;
        }

        public bool TryRegisterEventBoard<T>(T childBoard) where T : ChildEventBoardBase
        {
            var type = typeof(T);
            return _eventChildBoards.TryAdd(type, childBoard);
        }

        public bool TryGetEventBoard<T>(out T childBoard) where T : ChildEventBoardBase
        {
            if (_eventChildBoards.TryGetValue(typeof(T), out var raw) && raw is T typed)
            {
                childBoard = typed;
                return true;
            }

            childBoard = null;
            return false;
        }

        /// <summary>
        /// 登録済みの全ChildBoardへOnSceneChangedをfan-outする。
        /// Eventは値を永続化しないが、チャンネルの実体はChildEventBoardが握り続けるため、
        /// StateChildBoardと同様にシーンスコープの解除対象になる。
        /// </summary>
        public void OnSceneChanged(List<int> sceneIds)
        {
            for (int i = 0; i < sceneIds.Count; i++)
            {
                int sceneId = sceneIds[i];
                _sceneBoard.OnSceneChanged(sceneId);

                foreach (var childBoard in _stateChildBoards.Values)
                {
                    childBoard.OnSceneChanged(sceneId);
                }

                foreach (var childBoard in _eventChildBoards.Values)
                {
                    childBoard.OnSceneChanged(sceneId);
                }
            }
        }
    }
}