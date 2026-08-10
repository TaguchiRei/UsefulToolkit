using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// ChildBoardを型ごとに登録・取得する最上位のBlackBoard本体。
    /// シーンごとに1インスタンス、InitializationのContainer/Compositerが生成・登録する。
    /// </summary>
    public sealed class BlackBoard : IBlackBoard
    {
        private readonly Dictionary<Type, ChildStateBoardBase> _stateChildBoards = new();
        private readonly Dictionary<Type, ChildEventBoardBase> _eventChildBoards = new();

        public bool TryRegisterStateBoard<T>(T childBoard) where T : ChildStateBoardBase
        {
            var type = typeof(T);
            return _stateChildBoards.TryAdd(type, childBoard);
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
        /// 登録済みの全StateChildBoardへOnSceneChangedをfan-outする。EventChildBoardは
        /// 値を永続化しないためシーンスコープの解除対象がなく、対象外。
        /// </summary>
        public void OnSceneChanged(string sceneName)
        {
            foreach (var childBoard in _stateChildBoards.Values)
            {
                childBoard.OnSceneChanged(sceneName);
            }
        }
    }
}